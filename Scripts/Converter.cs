using System;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;

// TASKS
// . complete function return values
// . complete detailed description
// . remarks is in detailedDescription!
// . add GitHub links
// . layout/titling work

public enum WikiMode
{
    Unity = 0,
}

public static class Converter
{
    private static Dictionary<DefinitionNode, IDefinition> definitionNodeMap;

    private static StringBuilder outputLog;

    private static Exception lastRunException = null;

    // ---------[ FUNCTION MAPPING ]---------
    private delegate void ProcessNode(XmlNode xmlNode, DefinitionNode parentNode);

    // private static Dictionary<string, Action<XmlNode, IDefinition>> nodeActionMap = new Dictionary<string, Action<XmlNode, IDefinition>>
    private static Dictionary<string, ProcessNode> nodeActionMap = new Dictionary<string, ProcessNode>
    {
        { "compounddef", Converter.ProcessCompound },
        { "memberdef", Converter.ProcessMember },
        { "innerclass", Converter.ProcessInnerClass },
        { "innernamespace", Converter.ProcessInnerNamespace },
        { "sectiondef", Converter.ProcessSection },
        { "type", Converter.ProcessType },
        { "location", Converter.ProcessLocation },
        { "briefdescription", Converter.ProcessBriefDescription },
        { "detaileddescription", Converter.ProcessDetailedDescription },
        { "listofallmembers", Converter.ProcessListOfAllMembers },
        { "name", Converter.ProcessName },
        { "compoundname", Converter.ProcessCompoundName },
        { "basecompoundref", Converter.ProcessBaseCompoundRef },
        { "param", Converter.ProcessParam },
    };

    private static List<string> nodesToSkip = new List<string>()
    {
        "#text",
        "scope",
        "definition",
        "argsstring",
        "inheritancegraph",
        "collaborationgraph",
    };

    public delegate IDefinition CreateDefinitionDelegate(XmlNode xmlNode);

    private static Dictionary<string, CreateDefinitionDelegate> nodeKindCreationMap = new Dictionary<string, CreateDefinitionDelegate>()
    {
        { "namespace",  CreateNamespaceDefinition },
        { "property",   CreatePropertyDefinition },
        { "class",      (n) => new ClassDefinition() },
        { "struct",     (n) => new StructDefinition() },
        { "interface",  (n) => new InterfaceDefinition() },
        { "variable",   (n) => new VariableDefinition() },
        { "function",   (n) => new FunctionDefinition() },
        { "event",      (n) => new EventDefinition() },
        { "enum",       (n) => new EnumDefinition() },
        { "enumvalue",  (n) => new EnumValueDefinition() },
    };

    private static List<string> ignoredKinds = new List<string>()
    {
        "file",
        "dir",
        "page"
    };

    // ---------[ BASICS ]---------
    public static WikiMode mode = WikiMode.Unity;

    public static void RunOnFile(string filePath, string outputDirectory)
    {
        Converter.Initialize();

        try
        {
            Converter.ParseFile(filePath);
            Converter.BuildWiki(outputDirectory);
        }
        catch(Exception e)
        {
            lastRunException = e;
        }


        string logFilePath = (AppDomain.CurrentDomain.BaseDirectory
                              + "logs/log_" + DateTime.Now.ToFileTime() + ".txt");
        Converter.BuildLog(logFilePath);
        System.Diagnostics.Process.Start(logFilePath);
    }

    public static void RunOnDirectory(string directory, string outputDirectory)
    {
        Converter.Initialize();

        try
        {
            foreach(string filePath in Directory.GetFiles(directory, "*.xml", SearchOption.AllDirectories))
            {
                ParseFile(filePath);
            }
            Converter.BuildWiki(outputDirectory);
        }
        catch(Exception e)
        {
            lastRunException = e;
        }

        string logFilePath = (AppDomain.CurrentDomain.BaseDirectory
                              + "logs/log_" + DateTime.Now.ToFileTime() + ".txt");
        Converter.BuildLog(logFilePath);
        System.Diagnostics.Process.Start(logFilePath);
    }

    private static void Initialize()
    {
        outputLog = new StringBuilder();
        definitionNodeMap = new Dictionary<DefinitionNode, IDefinition>();
        lastRunException = null;
    }

    private static void ParseFile(string filePath)
    {
        outputLog.Append("\n--------------------\nParsing file: " + filePath + '\n');

        XmlDocument doc = new XmlDocument();
        doc.Load(filePath);

        ProcessChildNodes(doc.DocumentElement, null);
    }

    private static DefinitionNode FindDefinitionNodeById(string d_id)
    {
        foreach(DefinitionNode node in definitionNodeMap.Keys)
        {
            if(node.d_id == d_id) { return node; }
        }

        return null;
    }

    private static DefinitionNode FindDefinitionNodeByName(string name)
    {
        foreach(DefinitionNode node in definitionNodeMap.Keys)
        {
            if(node.name == name) { return node; }
        }

        return null;
    }

    private static DefinitionNode GetOrCreateNodeWithDefinition(string nodeId,
                                                                CreateDefinitionDelegate createDefinition,
                                                                XmlNode xmlNode)
    {
        // - get node -
        DefinitionNode node = FindDefinitionNodeById(nodeId);

        if(node == null)
        {
            outputLog.Append(". new node [" + nodeId + "]\n");

            node = new DefinitionNode();
            node.d_id = nodeId;

            definitionNodeMap.Add(node, null);
        }

        IDefinition definition = definitionNodeMap[node];
        if(definition == null)
        {
            outputLog.Append(". creating definition\n");

            definition = createDefinition(xmlNode);
            definition.node = node;

            definitionNodeMap[node] = definition;
        }

        return node;
    }

    private static IEnumerable<DefinitionNode> FindDefinitionNodesWithName(string name)
    {
        foreach(DefinitionNode node in definitionNodeMap.Keys)
        {
            if(node.name == name) { yield return node; }
        }
    }

    private static string ParseXMLString(string xmlString)
    {
        return xmlString.Replace("&lt; ", "[").Replace("&lt;", "[")
                        .Replace(" &gt;", "]").Replace("&gt;", "]")
                        .Replace("&apos;","\'");
    }

    private static TypeDescription ParseTypeDescription(XmlNode node)
    {
        XmlNode nodeClone = node.CloneNode(true);

        nodeClone.InnerXml = nodeClone.InnerXml.Replace("&lt;", "<template>")
                                               .Replace("&gt;", "</template>")
                                               .Replace("const ", "")
                                               .Replace("static ", "")
                                               .Replace("readonly ", "");

        outputLog.Append(". member type (INNERXML): " + nodeClone.InnerXml + '\n');

        if(nodeClone.FirstChild == null) { return null; }

        outputLog.Append(". member type (DEBUG): ");
        TypeDescription[] results = ProcessTypeNode(nodeClone.FirstChild);
        outputLog.Append('\n');


        TypeDescription td;
        if(results.Length == 1)
        {
            td = results[0];
        }
        else
        {
            outputLog.Append("WARNING: Unexpected type count returned (" + results.Length + ")\n");
            td = new TypeDescription();
        }

        outputLog.Append(". member type: " + td.name + '\n');

        return td;
    }

    private static TypeDescription[] ProcessTypeNode(XmlNode node)
    {
        TypeDescription[] retVal;

        switch(node.Name)
        {
            // Handled by previous node
            case "template":
            {
                outputLog.Append(".t");
            }
            return null;

            case "#text":
            {
                string[] typeNames = node.Value.Replace(" ", "").Split(',');
                retVal = new TypeDescription[typeNames.Length];

                for(int i = 0; i < typeNames.Length; ++i)
                {
                    retVal[i] = new TypeDescription()
                    {
                        name = typeNames[i],
                        isRef = false,
                    };

                    outputLog.Append(".I:" + retVal[i].name + ',');
                }

                outputLog.Length -= 1;
            }
            break;

            case "ref":
            {
                TypeDescription desc = new TypeDescription()
                {
                    name = node.InnerText,
                    d_id = node.Attributes["refid"].Value,
                    isRef = true,
                };

                retVal = new TypeDescription[1];
                retVal[0] = desc;

                outputLog.Append(".R:" + desc.name);
            }
            break;

            default:
            {
                outputLog.Append("WARNING: Unrecognised tag in type node (" + node.Name + ")\n");
                retVal = new TypeDescription[] { new TypeDescription() };
            }
            break;
        }

        if(node.NextSibling != null
           && node.NextSibling.Name == "template")
        {
            TypeDescription parentDesc = retVal[retVal.Length - 1];
            parentDesc.templatedTypes = new List<TypeDescription>();

            outputLog.Append(".T<");
            foreach(XmlNode childNode in node.NextSibling.ChildNodes)
            {
                if(childNode.Name != "template")
                {
                    parentDesc.templatedTypes.AddRange(ProcessTypeNode(childNode));
                }
            }
            outputLog.Append(">");
        }

        return retVal;
    }

    private static void BuildLog(string filePath)
    {
        if(lastRunException == null)
        {
            outputLog.Insert(0, "WIKIBUILD SUCCEEDED\n");
        }
        else
        {
            outputLog.Insert(0, "WIKIBUILD FAILED\n");

            outputLog.Append("\n!!! FAILED !!!\n"
                             + GenerateExceptionDebugString(lastRunException) + '\n');
        }

        Directory.CreateDirectory(Path.GetDirectoryName(filePath));

        File.WriteAllText(filePath, outputLog.ToString());

        UnityEngine.Debug.Log("---------[ " + filePath + " ]---------\n" + outputLog);
    }

    private static string GenerateExceptionDebugString(Exception e)
    {
        var debugString = new System.Text.StringBuilder();

        Exception baseException = e.GetBaseException();
        debugString.Append(baseException.GetType().Name + ": " + baseException.Message + "\n");

        var stackTrace = new System.Diagnostics.StackTrace(baseException, true);

        int frameCount = Math.Min(stackTrace.FrameCount, 6);
        for(int i = 0; i < frameCount; ++i)
        {
            var stackFrame = stackTrace.GetFrame(i);
            var method = stackFrame.GetMethod();

            debugString.Append(method.ReflectedType
                               + "." + method.Name + "(");

            var methodsParameters = method.GetParameters();
            foreach(var parameter in methodsParameters)
            {
                debugString.Append(parameter.ParameterType.Name + " "
                                   + parameter.Name + ", ");
            }
            if(methodsParameters.Length > 0)
            {
                debugString.Length -= 2;
            }

            debugString.Append(") @ " + stackFrame.GetFileName()
                               + ":" + stackFrame.GetFileLineNumber()
                               + "\n");
        }

        return debugString.ToString();
    }

    private static FunctionParameter GetOrCreateFunctionParameter(FunctionDefinition definition,
                                                                  string paramName)
    {
        foreach(FunctionParameter parameter in definition.parameters)
        {
            if(parameter.name == paramName)
            {
                outputLog.Append(". found exisiting param: " + paramName + '\n');
                return parameter;
            }
        }

        FunctionParameter newParam = new FunctionParameter();
        newParam.name = paramName;
        definition.parameters.Add(newParam);

        outputLog.Append(". added new param: " + paramName + '\n');

        return newParam;
    }

    // --------- [PROCESSING] ---------
    private static void ProcessChildNodes(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append("Processing children of " + xmlNode.Name + " for parent [" + (parentNode == null ? "NULL" : parentNode.d_id) + "]\n");

        foreach(XmlNode xmlChildNode in xmlNode.ChildNodes)
        {
            ProcessNode nodeAction;
            if(nodesToSkip.Contains(xmlChildNode.Name))
            {
                outputLog.Append("Ignoring " + xmlChildNode.Name + " node\n");
                continue;
            }
            else if(nodeActionMap.TryGetValue(xmlChildNode.Name, out nodeAction))
            {
                outputLog.Append("Processing " + xmlChildNode.Name + " node\n");
                nodeAction(xmlChildNode, parentNode);
            }
            else
            {
                outputLog.Append("WARNING: \"" + xmlChildNode.Name
                                 + "\" is unhandled node type\n");
            }
        }
    }

    // --- Basic Nodes ---
    private static void ProcessName(XmlNode node, DefinitionNode parentNode)
    {
        parentNode.name = ParseXMLString(node.InnerXml);
        outputLog.Append(". name is " + parentNode.name + '\n');
    }

    private static void ProcessCompoundName(XmlNode node, DefinitionNode parentNode)
    {
        string[] nameParts = ParseXMLString(node.InnerXml).Replace("::", ".").Split('.');
        parentNode.name = nameParts[nameParts.Length - 1];
        outputLog.Append(". name is " + parentNode.name + '\n');
    }

    private static void ProcessSection(XmlNode node, DefinitionNode parentNode)
    {
        outputLog.Append(". kind: " + node.Attributes["kind"].Value + '\n');
        ProcessChildNodes(node, parentNode);
    }

    private static void ProcessType(XmlNode node, DefinitionNode parentNode)
    {
        // skip if enum
        if(definitionNodeMap[parentNode] is EnumDefinition) { return; }

        MemberDefinition memberDefinition = definitionNodeMap[parentNode] as MemberDefinition;

        string typeString = node.InnerText;

        if(typeString.Contains("const "))
        {
            memberDefinition.isConst = true;
            memberDefinition.isStatic = (mode == WikiMode.Unity);
        }
        if(typeString.Contains("readonly "))
        {
            memberDefinition.isConst = true;
        }

        TypeDescription typeDescription = ParseTypeDescription(node);
        memberDefinition.type = typeDescription;
    }

    private static void ProcessParam(XmlNode xmlNode, DefinitionNode parentNode)
    {
        string paramName = ParseXMLString(xmlNode["declname"].InnerXml);

        FunctionDefinition definition = definitionNodeMap[parentNode] as FunctionDefinition;
        FunctionParameter parameter = GetOrCreateFunctionParameter(definition, paramName);

        parameter.type = ParseTypeDescription(xmlNode["type"]);
    }

    private static void ProcessLocation(XmlNode node, DefinitionNode parentNode)
    {
        outputLog.Append("WARNING: not implemented\n");
    }

    private static void ProcessDetailedDescription(XmlNode xmlNode, DefinitionNode parentNode)
    {
        foreach(XmlNode childNode in xmlNode)
        {
            ParseDetailedDescriptionNode(childNode, parentNode);
        }
    }

    private static void ParseDetailedDescriptionNode(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append(". parsing " + xmlNode.Name + " node\n");

        switch(xmlNode.Name)
        {
            case "para":
            {
                foreach(XmlNode childNode in xmlNode.ChildNodes)
                {
                    ParseDetailedDescriptionNode(childNode, parentNode);
                }
            }
            break;

            case "parameterlist":
            {
                FunctionDefinition definition = definitionNodeMap[parentNode] as FunctionDefinition;

                foreach(XmlNode childNode in xmlNode.ChildNodes)
                {
                    if(childNode.Name != "parameteritem")
                    {
                        outputLog.Append("WARNING: Unexpected child node ("
                                         + childNode.Name + ")\n");
                        continue;
                    }

                    ParseParamDescription(childNode, definition);
                }
            }
            break;

            case "simplesect":
            {
                if(xmlNode.Attributes["kind"].Value == "return")
                {
                    if(xmlNode.FirstChild != null)
                    {
                        FunctionDefinition definition = definitionNodeMap[parentNode] as FunctionDefinition;

                        StringBuilder sb = new StringBuilder();
                        ParseDescriptionXML(xmlNode.FirstChild, sb);

                        definition.returnDescription = sb.ToString().Trim();
                    }
                }
            }
            break;

            default:
            {
                outputLog.Append("WARNING: Unhandled detailed description child node\n");
            }
            return;
        }
    }

    private static void ParseParamDescription(XmlNode xmlNode, FunctionDefinition definition)
    {
        if(xmlNode["parameterdescription"].FirstChild != null)
        {
            string paramName = ParseXMLString(xmlNode["parameternamelist"]["parametername"].InnerXml);

            FunctionParameter parameter = GetOrCreateFunctionParameter(definition, paramName);

            StringBuilder sb = new StringBuilder();
            ParseDescriptionXML(xmlNode["parameterdescription"].FirstChild, sb);
            parameter.description = sb.ToString().Trim();
        }
    }

    private static void ProcessBaseCompoundRef(XmlNode xmlNode, DefinitionNode parentNode)
    {
        ObjectDefinition objectDefinition = definitionNodeMap[parentNode] as ObjectDefinition;
        TypeDescription typeDescription = ParseTypeDescription(xmlNode);
        bool isInterface = false;

        if(xmlNode.Attributes["refid"] != null)
        {
            string refId = xmlNode.Attributes["refid"].Value;
            typeDescription.d_id = refId;
            typeDescription.isRef = true;

            isInterface = (refId.Contains("interface_"));
        }

        if(isInterface)
        {
            objectDefinition.interfaces.Add(typeDescription);
            outputLog.Append(". set as interface\n");
        }
        else
        {
            objectDefinition.baseType = typeDescription;
            outputLog.Append(". set as base class\n");
        }
    }

    // --- Structure ---
    private static void ProcessCompound(XmlNode xmlNode, DefinitionNode parentNode)
    {
        string nodeKind = xmlNode.Attributes["kind"].Value;

        if(ignoredKinds.Contains(nodeKind))
        {
            outputLog.Append(". ignoring node of kind \'" + xmlNode.Name + "\'\n");
            return;
        }

        DefinitionNode compoundNode = GetOrCreateNodeWithDefinition(xmlNode.Attributes["id"].Value,
                                                                    nodeKindCreationMap[nodeKind],
                                                                    xmlNode);

        ProcessChildNodes(xmlNode, compoundNode);
    }

    private static void ProcessMember(XmlNode xmlNode, DefinitionNode parentNode)
    {
        string memberId = xmlNode.Attributes["id"].Value;
        string nodeKind = xmlNode.Attributes["kind"].Value;

        outputLog.Append(". kind: " + nodeKind + ", id: [" + memberId + "]\n");

        if(ignoredKinds.Contains(nodeKind))
        {
            outputLog.Append(". ignored kind\n");
            return;
        }

        // - set parenting -
        DefinitionNode memberNode = GetOrCreateNodeWithDefinition(xmlNode.Attributes["id"].Value,
                                                                  nodeKindCreationMap[nodeKind],
                                                                  xmlNode);

        if(memberNode.parent != null
           && memberNode.parent != parentNode)
        {
            outputLog.Append("WARNING: memberNode.parent ["
                             + memberNode.parent.d_id
                             + "] != parentNode ["
                             + (parentNode == null ? "NULL" : parentNode.d_id)
                             + "]\n");
        }
        memberNode.parent = parentNode;

        if(!parentNode.children.Contains(memberNode))
        {
            outputLog.Append(". adding member node [" + memberId + "] to ["
                             + parentNode.d_id + "]\n");

            parentNode.children.Add(memberNode);
        }

        // - grab attributes -
        MemberDefinition definition = definitionNodeMap[memberNode] as MemberDefinition;
        switch(xmlNode.Attributes["prot"].Value.ToLower())
        {
            case "public":
            {
                definition.protection = ProtectionLevel.Public;
            }
            break;
            case "private":
            {
                definition.protection = ProtectionLevel.Private;
            }
            break;
            case "protected":
            {
                definition.protection = ProtectionLevel.Protected;
            }
            break;
            case "package":
            {
                definition.protection = ProtectionLevel.Internal;
            }
            break;

            default:
            {
                outputLog.Append("WARNING: Unrecognized protection level \'"
                                 + xmlNode.Attributes["prot"].Value.ToLower()
                                 + "\'\n");
            }
            break;
        }

        definition.isStatic = (xmlNode.Attributes["static"].Value == "yes");

        ProcessChildNodes(xmlNode, memberNode);
    }

    private static void ProcessInnerClass(XmlNode xmlNode, DefinitionNode parentNode)
    {
        string nodeId = xmlNode.Attributes["refid"].Value;
        DefinitionNode innerClassNode = FindDefinitionNodeById(nodeId);
        if(innerClassNode == null)
        {
            outputLog.Append(". creating new node [" + nodeId + "]\n");

            innerClassNode = new DefinitionNode();
            innerClassNode.d_id = nodeId;

            definitionNodeMap.Add(innerClassNode, null);
        }

        if(innerClassNode.parent != null
           && innerClassNode.parent != parentNode)
        {
            outputLog.Append("WARNING: innerClassNode.parent ["
                             + DefinitionNode.GenerateFullName(innerClassNode.parent)
                             + "] != parentNode ["
                             + (parentNode == null ? "NULL" : DefinitionNode.GenerateFullName(parentNode))
                             + "]\n");
        }

        innerClassNode.parent = parentNode;
        parentNode.children.Add(innerClassNode);

        ProcessChildNodes(xmlNode, innerClassNode);
    }

    private static void ProcessInnerNamespace(XmlNode xmlNode, DefinitionNode parentNode)
    {
        string nodeId = xmlNode.Attributes["refid"].Value;
        DefinitionNode namespaceNode = FindDefinitionNodeById(nodeId);
        if(namespaceNode == null)
        {
            namespaceNode = new DefinitionNode();
            namespaceNode.d_id = nodeId;

            definitionNodeMap.Add(namespaceNode, null);
        }

        if(namespaceNode.parent != null
           && namespaceNode.parent != parentNode)
        {
            outputLog.Append("WARNING: namespaceNode.parent ["
                             + DefinitionNode.GenerateFullName(namespaceNode.parent)
                             + "] != parentNode ["
                             + (parentNode == null ? "NULL" : DefinitionNode.GenerateFullName(parentNode))
                             + "]\n");
        }

        namespaceNode.parent = parentNode;
        parentNode.children.Add(namespaceNode);

        ProcessChildNodes(xmlNode, namespaceNode);
    }

    private static void ProcessListOfAllMembers(XmlNode xmlNode, DefinitionNode parentNode)
    {
        // - instantiate member nodes -
        foreach(XmlNode memberNode in xmlNode.ChildNodes)
        {
            string memberId = memberNode.Attributes["refid"].Value;
            DefinitionNode mDefNode = FindDefinitionNodeById(memberId);

            if(mDefNode == null)
            {
                mDefNode = new DefinitionNode();
                mDefNode.d_id = memberId;

                definitionNodeMap.Add(mDefNode, null);

            }

            if(!parentNode.children.Contains(mDefNode))
            {
                outputLog.Append(". adding member node [" + memberId + "]");

                parentNode.children.Add(mDefNode);
            }

            ProcessChildNodes(memberNode, mDefNode);
        }
    }

    private static void ProcessEnumValue(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append("WARNING: not implemented\n");
    }

    private static void ProcessBriefDescription(XmlNode xmlNode, DefinitionNode parentNode)
    {
        if(xmlNode.FirstChild != null)
        {
            StringBuilder retVal = new StringBuilder();
            ParseDescriptionXML(xmlNode.FirstChild, retVal);

            IDefinition definition = definitionNodeMap[parentNode];
            definition.briefDescription = retVal.ToString().Trim();
        }
    }

    private static void ParseDescriptionXML(XmlNode node, StringBuilder result)
    {
        bool isTagUnrecognised = false;

        // - open tag -
        switch(node.Name)
        {
            case "para":
            case "#text":
            break;

            case "ulink":
            {
                result.Append('[');
            }
            break;

            case "ref":
            {
                result.Append("[[");
            }
            break;

            default:
            {
                outputLog.Append("WARNING: Unrecognized tag in description (" + node.Name + ")\n");
                isTagUnrecognised = true;

                result.Append("!START." + node.Name + "!");
            }
            break;
        }

        // - innards -
        if(node.HasChildNodes)
        {
            foreach(XmlNode childNode in node.ChildNodes)
            {
                ParseDescriptionXML(childNode, result);
            }
        }
        else if(node.Value != null)
        {
            result.Append(ParseXMLString(node.Value));
        }

        // - close tag -
        if(isTagUnrecognised)
        {
            result.Append("!END." + node.Name + "!");
        }
        else
        {
            switch(node.Name)
            {
                case "para":
                {
                    result.Append("\n\n");
                }
                break;

                case "ulink":
                {
                    result.Append("](" + node.Attributes["url"].Value + ")");
                }
                break;

                case "ref":
                {
                    result.Append("]]");
                }
                break;
            }
        }
    }

    // ---------[ KIND CREATION ]---------
    private static IDefinition CreateNamespaceDefinition(XmlNode xmlNode)
    {
        NamespaceDefinition definition = new NamespaceDefinition();
        definition.language = xmlNode.Attributes["language"].Value;
        return definition;
    }

    private static IDefinition CreatePropertyDefinition(XmlNode xmlNode)
    {
        PropertyDefinition definition = new PropertyDefinition();

        // - getter -
        if(xmlNode.Attributes["gettable"].Value == "yes")
        {
            definition.getterProtection = ProtectionLevel.Public;
        }
        else if(xmlNode.Attributes["privategettable"].Value == "yes")
        {
            definition.getterProtection = ProtectionLevel.Private;
        }
        else if(xmlNode.Attributes["protectedgettable"].Value == "yes")
        {
            definition.getterProtection = ProtectionLevel.Protected;
        }

        // - setter -
        if(xmlNode.Attributes["settable"].Value == "yes")
        {
            definition.setterProtection = ProtectionLevel.Public;
        }
        else if(xmlNode.Attributes["privatesettable"].Value == "yes")
        {
            definition.setterProtection = ProtectionLevel.Private;
        }
        else if(xmlNode.Attributes["protectedsettable"].Value == "yes")
        {
            definition.setterProtection = ProtectionLevel.Protected;
        }

        return definition;
    }

    // ---------[ BUILD WIKI ]---------
    private static void BuildWiki(string outputDirectory)
    {
        outputLog.Append("\nSTART BUILDING WIKI\n");

        if(outputDirectory[outputDirectory.Length - 1] != '/')
        {
            outputDirectory += "/";
        }

        outputDirectory += "generated_pages/";
        Directory.CreateDirectory(outputDirectory);

        CreateAPIIndex(outputDirectory);

        // - sort nodes -
        List<DefinitionNode> classNodes = new List<DefinitionNode>();
        List<DefinitionNode> interfaceNodes = new List<DefinitionNode>();
        List<DefinitionNode> enumNodes = new List<DefinitionNode>();
        List<DefinitionNode> functionNodes = new List<DefinitionNode>();
        List<DefinitionNode> propertyNodes = new List<DefinitionNode>();
        List<DefinitionNode> variableNodes = new List<DefinitionNode>();
        List<DefinitionNode> eventNodes = new List<DefinitionNode>();

        foreach(var kvp in definitionNodeMap)
        {
            if(kvp.Value is ClassDefinition)
            {
                classNodes.Add(kvp.Key);
            }
            else if(kvp.Value is InterfaceDefinition)
            {
                interfaceNodes.Add(kvp.Key);
            }
            else if(kvp.Value is EnumDefinition)
            {
                enumNodes.Add(kvp.Key);
            }
            else if(kvp.Value is FunctionDefinition)
            {
                functionNodes.Add(kvp.Key);
            }
            else if(kvp.Value is PropertyDefinition)
            {
                propertyNodes.Add(kvp.Key);
            }
            else if(kvp.Value is VariableDefinition)
            {
                variableNodes.Add(kvp.Key);
            }
            else if(kvp.Value is EventDefinition)
            {
                eventNodes.Add(kvp.Key);
            }
            else
            {
                outputLog.Append("WARNING: Unhandled definition type ("
                                 + kvp.Value.GetType().ToString()
                                 + ")\n");
            }
        }

        // - output indicies -
        foreach(DefinitionNode classNode in classNodes)
        {
            CreateClassIndex(classNode, outputDirectory);
        }

        foreach(DefinitionNode interfaceNode in interfaceNodes)
        {
            CreateInterfaceIndex(interfaceNode, outputDirectory);
        }

        foreach(DefinitionNode enumNode in enumNodes)
        {
            CreateEnumIndex(enumNode, outputDirectory);
        }

        foreach(DefinitionNode functionNode in functionNodes)
        {
            CreateFunctionIndex(functionNode, outputDirectory);
        }

        foreach(DefinitionNode propertyNode in propertyNodes)
        {
            CreatePropertyIndex(propertyNode, outputDirectory);
        }

        foreach(DefinitionNode variableNode in variableNodes)
        {
            CreateVariableIndex(variableNode, outputDirectory);
        }

        foreach(DefinitionNode eventNode in eventNodes)
        {
            CreateEventIndex(eventNode, outputDirectory);
        }

        outputLog.Append("END BUILDING WIKI\n");
    }

    private static List<string> namespacesLanguagesToInclude = new List<string>()
    {
        "c#",
    };

    private static void CreateAPIIndex(string outputDirectory)
    {
        outputLog.Append("\n--------------------\nCreating API Index\n");

        List<string> lines = new List<string>(definitionNodeMap.Count + 20);
        List<DefinitionNode> rootNodes = new List<DefinitionNode>();

        foreach(var kvp in definitionNodeMap)
        {
            NamespaceDefinition definition = kvp.Value as NamespaceDefinition;

            if(kvp.Key.parent == null
               && definition != null
               && namespacesLanguagesToInclude.Contains(definition.language.ToLower()))
            {
                rootNodes.Add(kvp.Key);

                outputLog.Append(". found root node: " + kvp.Key.name);
            }
        }

        while(rootNodes.Count > 0)
        {
            DefinitionNode node = rootNodes[0];
            rootNodes.RemoveAt(0);

            outputLog.Append(". new namespace section: " + node.name);

            StringBuilder fullNamespace = new StringBuilder();
            DefinitionNode nameNode = node;
            while(nameNode != null)
            {                fullNamespace.Insert(0, nameNode.name + ".");
                nameNode = nameNode.parent;
            }
            fullNamespace.Length -= 1;

            lines.Add("## Namespace: " + fullNamespace.ToString() + '\n');

            lines.Add("| Class | Description |");
            lines.Add("| - | - |");

            foreach(var childNode in node.children)
            {
                if(definitionNodeMap[childNode] is NamespaceDefinition)
                {
                    rootNodes.Insert(0, childNode);
                    outputLog.Append(". found inner namespace node: " + childNode.name);
                }
                else
                {
                    lines.Add("| [" + childNode.name + "]("
                              + DefinitionNode.GenerateFullName(childNode) + ") "
                              + "| " + definitionNodeMap[childNode].briefDescription + " |");
                }
            }

            lines.Add("");
        }

        string outputFilePath = outputDirectory + "API Index.md";
        File.WriteAllLines(outputFilePath, lines.ToArray());
    }

    // ref: https://docs.unity3d.com/ScriptReference/UI.Button.html
    private static void CreateClassIndex(DefinitionNode classNode,
                                         string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(classNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Class Index: " + filePath + '\n');
        outputLog.Append(". id [" + classNode.d_id + "]\n");

        // - collect data -
        ClassDefinition classDefinition = definitionNodeMap[classNode] as ClassDefinition;
        List<DefinitionNode> const_props = new List<DefinitionNode>();
        List<DefinitionNode> static_props = new List<DefinitionNode>();
        List<DefinitionNode> props = new List<DefinitionNode>();
        List<DefinitionNode> const_methods = new List<DefinitionNode>();
        List<DefinitionNode> static_methods = new List<DefinitionNode>();
        List<DefinitionNode> methods = new List<DefinitionNode>();

        foreach(var childNode in classNode.children)
        {
            outputLog.Append(". processing " + childNode.name + '\n');

            MemberDefinition memberDef = definitionNodeMap[childNode] as MemberDefinition;

            if(memberDef is PropertyDefinition)
            {
                if(memberDef.isConst)
                {
                    const_props.Add(childNode);
                }
                else if(memberDef.isStatic)
                {
                    static_props.Add(childNode);
                }
                else
                {
                    props.Add(childNode);
                }
            }
            else if(memberDef is FunctionDefinition)
            {
                if(memberDef.isConst)
                {
                    const_methods.Add(childNode);
                }
                else if(memberDef.isStatic)
                {
                    static_methods.Add(childNode);
                }
                else
                {
                    methods.Add(childNode);
                }
            }
            else
            {
                outputLog.Append("WARNING: " + childNode.name
                                 + " is an unrecognized member type ("
                                 + (memberDef == null ? "NON-MEMBER" : memberDef.GetType().ToString())
                                 + "). Ignoring.\n");
            }
        }

        // - build index -
        List<string> lines = new List<string>();
        bool isStaticClass = (props.Count == 0 && methods.Count == 0);

        lines.Add("# " + classNode.name + "\n");
        lines.Add((isStaticClass ? "static " : "")
                  + "class in " + DefinitionNode.GenerateFullName(classNode.parent)
                  + '\n');

        if(classDefinition.baseType != null)
        {
            lines.Add("inherits from: " + GenerateTypeMDString(classDefinition.baseType) + '\n');
        }

        if(classDefinition.interfaces.Count > 0)
        {
            StringBuilder interfaceString = new StringBuilder();
            interfaceString.Append("implements interfaces: ");

            foreach(TypeDescription interfaceType in classDefinition.interfaces)
            {
                interfaceString.Append(GenerateTypeMDString(interfaceType) + ", ");
            }

            interfaceString.Length -= 2;
            interfaceString.Append('\n');

            lines.Add(interfaceString.ToString());
        }

        if(!String.IsNullOrEmpty(classDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(classDefinition.briefDescription);
        }

        if(const_props.Count > 0)
        {
            lines.Add("\n# Constant Properties");
            lines.AddRange(GenerateMemberTable(const_props));
        }

        if(static_props.Count > 0)
        {
            lines.Add("\n# Static Properties");
            lines.AddRange(GenerateMemberTable(static_props));
        }

        if(props.Count > 0)
        {
            lines.Add("\n# Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        if(const_methods.Count > 0)
        {
            lines.Add("\n# Constant Methods");
            lines.AddRange(GenerateMemberTable(const_methods));
        }

        if(static_methods.Count > 0)
        {
            lines.Add("\n# Static Methods");
            lines.AddRange(GenerateMemberTable(static_methods));
        }

        if(methods.Count > 0)
        {
            lines.Add("\n# Public Methods");
            lines.AddRange(GenerateMemberTable(methods));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateInterfaceIndex(DefinitionNode interfaceNode,
                                             string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(interfaceNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Interface Index: " + filePath + '\n');
        outputLog.Append(". id [" + interfaceNode.d_id + "]\n");

        // - collect data -
        InterfaceDefinition interfaceDefinition = definitionNodeMap[interfaceNode] as InterfaceDefinition;
        List<DefinitionNode> props = new List<DefinitionNode>();
        List<DefinitionNode> methods = new List<DefinitionNode>();

        foreach(var childNode in interfaceNode.children)
        {
            outputLog.Append(". processing " + childNode.name + '\n');

            MemberDefinition memberDef = definitionNodeMap[childNode] as MemberDefinition;

            if(memberDef is PropertyDefinition)
            {
                props.Add(childNode);
            }
            else if(memberDef is FunctionDefinition)
            {
                methods.Add(childNode);
            }
            else
            {
                outputLog.Append("WARNING: " + childNode.name
                                 + " is an unrecognized member type ("
                                 + (memberDef == null ? "NON-MEMBER" : memberDef.GetType().ToString())
                                 + "). Ignoring.\n");
            }
        }

        // - build index -
        List<string> lines = new List<string>();

        lines.Add("# " + interfaceNode.name + "\n");
        lines.Add("interface in " + DefinitionNode.GenerateFullName(interfaceNode.parent));

        if(!String.IsNullOrEmpty(interfaceDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(interfaceDefinition.briefDescription);
        }

        if(props.Count > 0)
        {
            lines.Add("\n# Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        if(methods.Count > 0)
        {
            lines.Add("\n# Methods");
            lines.AddRange(GenerateMemberTable(methods));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateEnumIndex(DefinitionNode enumNode,
                                        string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(enumNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Enum Index: " + filePath + '\n');

        // - collect data -
        EnumDefinition enumDefinition = definitionNodeMap[enumNode] as EnumDefinition;
        List<DefinitionNode> props = new List<DefinitionNode>();

        foreach(var childNode in enumNode.children)
        {
            outputLog.Append(". processing " + childNode.name + '\n');

            MemberDefinition valueDefinition = definitionNodeMap[childNode] as MemberDefinition;

            if(valueDefinition != null)
            {
                props.Add(childNode);
            }
            else
            {
                outputLog.Append("WARNING: Unexpected child node under enum node (" +
                                 childNode.name + ")\n");
            }
        }

        // - build index -
        List<string> lines = new List<string>();

        lines.Add("# " + enumNode.name + "\n");
        lines.Add("enumeration in " + DefinitionNode.GenerateFullName(enumNode.parent));

        if(!String.IsNullOrEmpty(enumDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(enumDefinition.briefDescription);
        }

        if(props.Count > 0)
        {
            lines.Add("\n# Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static List<string> GenerateMemberTable(IEnumerable<DefinitionNode> memberEntryNodes)
    {
        List<string> retVal = new List<string>();

        retVal.Add("| Name | Description |");
        retVal.Add("| - | - |");

        foreach(var memberNode in memberEntryNodes)
        {
            MemberDefinition memberDef = definitionNodeMap[memberNode] as MemberDefinition;
            retVal.Add("| [" + memberNode.name + "](" + DefinitionNode.GenerateFullName(memberNode) + ") "
                       + "| " + memberDef.briefDescription + " |");
        }

        return retVal;
    }

    private static void CreateVariableIndex(DefinitionNode variableNode,
                                            string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(variableNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Variable Index: " + filePath + '\n');

        VariableDefinition variableDefinition = definitionNodeMap[variableNode] as VariableDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# [" + variableNode.parent.name + "]("
                  + DefinitionNode.GenerateFullName(variableNode.parent)
                  + ")." + variableNode.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(variableDefinition.protection.ToString().ToLower() + " ");

        if(variableDefinition.isConst)
        {
            buildString.Append("const ");
        }
        else if(variableDefinition.isStatic)
        {
            buildString.Append("static ");
        }

        buildString.Append(GenerateTypeMDString(variableDefinition.type) + ' ');

        buildString.Append(variableNode.name + ";\n");

        lines.Add(buildString.ToString());

        // - description -
        if(!String.IsNullOrEmpty(variableDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(variableDefinition.briefDescription + '\n');
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateEventIndex(DefinitionNode eventNode,
                                         string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(eventNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Event Index: " + filePath + '\n');

        EventDefinition eventDefinition = definitionNodeMap[eventNode] as EventDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# [" + eventNode.parent.name + "]("
                  + DefinitionNode.GenerateFullName(eventNode.parent)
                  + ")." + eventNode.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(eventDefinition.protection.ToString().ToLower() + " ");

        if(eventDefinition.isStatic)
        {
            buildString.Append("static ");
        }

        buildString.Append("event " + GenerateTypeMDString(eventDefinition.type)
                           + ' ' + eventNode.name + ";\n");

        lines.Add(buildString.ToString());

        // - description -
        if(!String.IsNullOrEmpty(eventDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(eventDefinition.briefDescription + '\n');
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreatePropertyIndex(DefinitionNode propertyNode,
                                            string outputDirectory)
    {
        string fileName = DefinitionNode.GenerateFullName(propertyNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Property Index: " + filePath + '\n');

        PropertyDefinition propertyDefinition = definitionNodeMap[propertyNode] as PropertyDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# [" + propertyNode.parent.name + "]("
                  + DefinitionNode.GenerateFullName(propertyNode.parent)
                  + ")." + propertyNode.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(propertyDefinition.protection.ToString().ToLower() + " ");

        if(propertyDefinition.isConst)
        {
            buildString.Append("readonly ");
        }
        if(propertyDefinition.isStatic)
        {
            buildString.Append("static ");
        }

        if(propertyDefinition.type != null)
        {
            buildString.Append(GenerateTypeMDString(propertyDefinition.type) + ' ');
        }

        buildString.Append(propertyNode.name + " {");

        if(propertyDefinition.getterProtection != ProtectionLevel._UNKNOWN_)
        {
            buildString.Append(" "
                               + propertyDefinition.getterProtection.ToString().ToLower()
                               + " get;");
        }
        if(propertyDefinition.setterProtection != ProtectionLevel._UNKNOWN_)
        {
            buildString.Append(" "
                               + propertyDefinition.setterProtection.ToString().ToLower()
                               + " set;");
        }

        buildString.Append(" }\n");

        lines.Add(buildString.ToString());

        // - description -
        if(!String.IsNullOrEmpty(propertyDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(propertyDefinition.briefDescription + '\n');
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateFunctionIndex(DefinitionNode functionNode,
                                            string outputDirectory)
    {
        // - Static Function Layout -
        // Class.propertyName
        //
        // definition
        //
        // # Parameters
        // {PARAMETER TABLE}
        //
        // # Description
        // BriefDescription
        // DetailedDescription
        //
        // Example
        //
        // SeeAlso:

        string fileName = DefinitionNode.GenerateFullName(functionNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Function Index: " + filePath + '\n');

        FunctionDefinition functionDefinition = definitionNodeMap[functionNode] as FunctionDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# [" + functionNode.parent.name + "]("
                  + DefinitionNode.GenerateFullName(functionNode.parent)
                  + ")." + functionNode.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(functionDefinition.protection.ToString().ToLower() + " ");

        if(functionDefinition.isStatic)
        {
            buildString.Append("static ");
        }

        buildString.Append(GenerateTypeMDString(functionDefinition.type) + ' ');
        buildString.Append(functionNode.name + '(');

        if(functionDefinition.parameters.Count > 0)
        {
            foreach(FunctionParameter parameter in functionDefinition.parameters)
            {
                buildString.Append(GenerateTypeMDString(parameter.type)
                                   + ' ' + parameter.name + ", ");
            }

            buildString.Length -= 2;
        }

        buildString.Append(");\n");
        lines.Add(buildString.ToString());

        // - parameters -
        if(functionDefinition.parameters.Count > 0)
        {
            lines.Add("## Parameters\n");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");
            foreach(FunctionParameter parameter in functionDefinition.parameters)
            {
                lines.Add("| " + parameter.name + " | "
                          + parameter.description + " |");
            }
        }

        // - returns -
        if(functionDefinition.type.name != "void")
        {
            lines.Add("\n## Returns\n");
            if(String.IsNullOrEmpty(functionDefinition.returnDescription))
            {
                lines.Add("**" + GenerateTypeMDString(functionDefinition.type)
                          + "**: Result of the function calculations");
            }
            else
            {
                lines.Add("**" + GenerateTypeMDString(functionDefinition.type)
                          + "**: " + functionDefinition.returnDescription);
            }
        }

        // - description -
        if(!String.IsNullOrEmpty(functionDefinition.briefDescription))
        {
            lines.Add("\n## Description\n");
            lines.Add(functionDefinition.briefDescription + '\n');
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static string GenerateTypeMDString(TypeDescription typeDesc)
    {
        StringBuilder sb = new StringBuilder();

        if(typeDesc.isRef)
        {
            outputLog.Append(". resolving type ref id [" + typeDesc.d_id + "]\n");

            DefinitionNode typeNode = FindDefinitionNodeById(typeDesc.d_id);
            sb.Append("[" + typeNode.name + "](" + DefinitionNode.GenerateFullName(typeNode) + ")");
        }
        else
        {
            sb.Append(typeDesc.name);
        }

        if(typeDesc.templatedTypes != null)
        {
            sb.Append('[');

            foreach(TypeDescription innerDesc in typeDesc.templatedTypes)
            {
                sb.Append(GenerateTypeMDString(innerDesc) + ',');
            }

            sb.Length -= 1;

            sb.Append(']');
        }

        return sb.ToString();
    }
}
