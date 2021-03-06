﻿using System;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;

// TASKS
// . put definitions into codeblocks?
// . output Structs and Namespaces
// . add GitHub links
// . layout/titling work
// . support location
// . support derivedcompoundref
// . support "this" param
// . inbodydescription?

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
        { "briefdescription", Converter.ProcessBriefDescription },
        { "detaileddescription", Converter.ProcessDetailedDescription },
        { "listofallmembers", Converter.ProcessListOfAllMembers },
        { "name", Converter.ProcessName },
        { "compoundname", Converter.ProcessCompoundName },
        { "basecompoundref", Converter.ProcessBaseCompoundRef },
        { "param", Converter.ProcessParam },
        { "initializer", Converter.ProcessInitializer },
        { "enumvalue", Converter.ProcessEnumValue },
    };

    private static List<string> nodesToSkip = new List<string>()
    {
        "#text",
        "scope",
        "definition",
        "argsstring",
        "inheritancegraph",
        "collaborationgraph",
        "inbodydescription",
        "location",
        "derivedcompoundref",
        "templateparamlist",
        "reimplements",
        "reimplementedby",
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
                if(Path.GetFileName(filePath) != "index.xml")
                {
                    ParseFile(filePath);
                }
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

    private static TypeDescription ParseTypeDescription(XmlNode node)
    {
        XmlNode nodeClone = node.CloneNode(true);

        nodeClone.InnerXml = nodeClone.InnerXml.Replace("&lt;", "<template>")
                                               .Replace("&gt;", "</template>")
                                               .Replace("const", "")
                                               .Replace("static", "")
                                               .Replace("readonly", "")
                                               .Replace("out ", "")
                                               .Replace("<ref ", "__R__")
                                               .Replace("ref ", "")
                                               .Replace("__R__", "<ref ")
                                               .Replace("[]", "<array/>");

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

        bool isTemplated = (node.NextSibling != null
                            && node.NextSibling.Name == "template");

        bool isArray = (node.NextSibling != null && node.NextSibling.Name == "array");
        isArray |= (isTemplated && node.NextSibling.NextSibling != null
                    && node.NextSibling.NextSibling.Name == "array");

        switch(node.Name)
        {
            // Handled by previous node
            case "template":
            {
                outputLog.Append(".t");
            }
            return null;
            case "array":
            {
                outputLog.Append(".a");
            }
            return null;

            case "#text":
            {
                string[] typeNames = node.Value.Replace(" ", "").Split(',');
                retVal = new TypeDescription[typeNames.Length];

                for(int i = 0; i < typeNames.Length; ++i)
                {
                    bool isThisTypeArray = (isArray && i == typeNames.Length - 1);

                    retVal[i] = new TypeDescription()
                    {
                        name = typeNames[i],
                        isRef = false,
                        isArray = isThisTypeArray,
                    };

                    outputLog.Append(".I:" + retVal[i].name
                                     + (isThisTypeArray ? "[ARRAY]" : "")
                                     + ',');
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
                desc.isArray = isArray;

                retVal = new TypeDescription[1];
                retVal[0] = desc;

                outputLog.Append(".R:" + desc.name
                                 + (isArray ? "[ARRAY]" : ""));
            }
            break;

            default:
            {
                outputLog.Append("WARNING: Unrecognised tag in type node (" + node.Name + ")\n");
                retVal = new TypeDescription[] { new TypeDescription() };
            }
            break;
        }

        if(isTemplated)
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
        parentNode.name = node.InnerText.Trim().Replace("< ", "<").Replace(" >", ">");
        outputLog.Append(". name is " + parentNode.name + '\n');
    }

    private static void ProcessCompoundName(XmlNode node, DefinitionNode parentNode)
    {
        string[] nameParts = node.InnerText.Replace("::", ".").Split('.');
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
        string paramName = xmlNode["declname"].InnerText;

        FunctionDefinition definition = definitionNodeMap[parentNode] as FunctionDefinition;
        FunctionParameter parameter = GetOrCreateFunctionParameter(definition, paramName);

        parameter.type = ParseTypeDescription(xmlNode["type"]);
    }

    private static void ProcessDetailedDescription(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append(". detailed description (DEBUG): ");

        IDefinition definition = definitionNodeMap[parentNode];

        StringBuilder sb = new StringBuilder();
        ParseDescriptionXML(xmlNode, definition, sb);
        definition.remarks = sb.ToString().Trim();
    }

    private static void ParseDescriptionXML(XmlNode node,
                                            IDefinition definition,
                                            StringBuilder currentString)
    {
        outputLog.Append("START:" + node.Name + '-');

        // - open tag -
        switch(node.Name)
        {
            // ignore
            case "parameternamelist":
            return;

            case "detaileddescription":
            case "briefdescription":
            case "para":
            case "parameterdescription":
            case "#text":
            break;

            case "ulink":
            {
                currentString.Append('[');
            }
            break;

            case "ref":
            {
                currentString.Append("[[");
            }
            break;

            case "parameterlist":
            {
                if(!(definition is FunctionDefinition))
                {
                    outputLog.Append("\nWARNING: Definition is not a function definition\n");
                    return;
                }

                if(node.Attributes["kind"].Value != "param")
                {
                    outputLog.Append("\nWARNING: Unhandled parameterlist kind ("
                                     + node.Attributes["kind"].Value + ")\n");
                    return;
                }
            }
            break;

            case "parameteritem":
            {
                if(node.ChildNodes.Count != 2)
                {
                    outputLog.Append("\nWARNING: Unexpected child count = " + node.ChildNodes.Count + '\n');
                }

                if(node["parameternamelist"].ChildNodes.Count != 1)
                {
                    outputLog.Append("\nWARNING: Unexpected child count for parameternamelist = "
                                     + node["parameternamelist"].ChildNodes.Count + '\n');
                }

                currentString = new StringBuilder();
            }
            break;

            case "simplesect":
            {
                if(!(definition is FunctionDefinition))
                {
                    outputLog.Append("\nWARNING: Definition is not a function definition\n");
                    return;
                }

                if(node.Attributes["kind"].Value != "return")
                {
                    outputLog.Append("\nWARNING: Unhandled simplesect kind ("
                                     + node.Attributes["kind"].Value + ")\n");
                    return;
                }

                currentString = new StringBuilder();
            }
            break;

            case "itemizedlist":
            {
                currentString.Append('\n');
            }
            break;

            case "listitem":
            {
                currentString.Append("- ");
            }
            break;

            default:
            {
                outputLog.Append("\nWARNING: Unrecognized tag in description (" + node.Name + ")\n");
            }
            break;
        }

        // - innards -
        if(node.HasChildNodes)
        {
            foreach(XmlNode childNode in node.ChildNodes)
            {
                ParseDescriptionXML(childNode, definition, currentString);
            }
        }
        else if(node.Value != null)
        {
            currentString.Append(node.InnerText);
        }

        // - close tag -
        switch(node.Name)
        {
            case "para":
            {
                currentString.Append("\n\n");
            }
            break;

            case "ulink":
            {
                currentString.Append("](" + node.Attributes["url"].Value + ")");
            }
            break;

            case "ref":
            {
                currentString.Append("]]");
            }
            break;

            case "parameteritem":
            {
                string paramName = node["parameternamelist"]["parametername"].InnerText;

                FunctionDefinition functionDefinition = definition as FunctionDefinition;

                FunctionParameter param = GetOrCreateFunctionParameter(functionDefinition, paramName);
                param.description = currentString.ToString().Trim();
            }
            break;

            case "simplesect":
            {
                FunctionDefinition functionDefinition = definition as FunctionDefinition;
                functionDefinition.returnDescription = currentString.ToString().Trim();
            }
            break;

            case "listitem":
            {
                currentString.Length -= 1; // remove extra '\n'
            }
            break;
        }

        outputLog.Append("END:" + node.Name + '-');
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
            outputLog.Append(". ignoring node of kind \'" + nodeKind + "\'\n");
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
        string valueId = xmlNode.Attributes["id"].Value;

        // - set parenting -
        DefinitionNode valueNode = GetOrCreateNodeWithDefinition(xmlNode.Attributes["id"].Value,
                                                                 nodeKindCreationMap["enumvalue"],
                                                                 xmlNode);

        if(valueNode.parent != null
           && valueNode.parent != parentNode)
        {
            outputLog.Append("WARNING: valueNode.parent ["
                             + valueNode.parent.d_id
                             + "] != parentNode ["
                             + (parentNode == null ? "NULL" : parentNode.d_id)
                             + "]\n");
        }
        valueNode.parent = parentNode;

        if(!parentNode.children.Contains(valueNode))
        {
            outputLog.Append(". adding enum value [" + valueId + "] to ["
                             + parentNode.d_id + "]\n");

            parentNode.children.Add(valueNode);
        }

        ProcessChildNodes(xmlNode, valueNode);
    }

    private static void ProcessBriefDescription(XmlNode xmlNode, DefinitionNode parentNode)
    {
        IDefinition definition = definitionNodeMap[parentNode];
        StringBuilder retVal = new StringBuilder();
        ParseDescriptionXML(xmlNode, definition, retVal);

        definition.briefDescription = retVal.ToString().Trim();
    }

    private static void ProcessInitializer(XmlNode node, DefinitionNode parentNode)
    {
        if(node.FirstChild == null) { return; }

        outputLog.Append(". adding initializedValue\n");
        MemberDefinition definition = definitionNodeMap[parentNode] as MemberDefinition;
        definition.initializedValue = node.InnerText.Substring(2).Replace("<", "[").Replace(">", "]").Trim(); // skip "= "
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
        List<DefinitionNode> structNodes = new List<DefinitionNode>();
        List<DefinitionNode> interfaceNodes = new List<DefinitionNode>();
        List<DefinitionNode> enumNodes = new List<DefinitionNode>();
        List<DefinitionNode> enumValueNodes = new List<DefinitionNode>();
        List<DefinitionNode> functionNodes = new List<DefinitionNode>();
        List<DefinitionNode> propertyNodes = new List<DefinitionNode>();
        List<DefinitionNode> variableNodes = new List<DefinitionNode>();
        List<DefinitionNode> eventNodes = new List<DefinitionNode>();

        foreach(var kvp in definitionNodeMap)
        {
            if(kvp.Value.protection == ProtectionLevel.Private)
            {
                outputLog.Append(". ignoring private definition: " + kvp.Key.name + '\n');
                continue;
            }
            else if(kvp.Value is NamespaceDefinition)
            {
                outputLog.Append(". ignoring namespace definition: " + kvp.Key.name + '\n');
                continue;
            }
            else if(kvp.Value is ClassDefinition)
            {
                classNodes.Add(kvp.Key);
            }
            else if(kvp.Value is StructDefinition)
            {
                structNodes.Add(kvp.Key);
            }
            else if(kvp.Value is InterfaceDefinition)
            {
                interfaceNodes.Add(kvp.Key);
            }
            else if(kvp.Value is EnumDefinition)
            {
                enumNodes.Add(kvp.Key);
            }
            else if(kvp.Value is EnumValueDefinition)
            {
                enumValueNodes.Add(kvp.Key);
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

        foreach(DefinitionNode structNode in structNodes)
        {
            CreateStructIndex(structNode, outputDirectory);
        }

        foreach(DefinitionNode interfaceNode in interfaceNodes)
        {
            CreateInterfaceIndex(interfaceNode, outputDirectory);
        }

        foreach(DefinitionNode enumNode in enumNodes)
        {
            CreateEnumIndex(enumNode, outputDirectory);
        }

        foreach(DefinitionNode enumValueNode in enumValueNodes)
        {
            CreateEnumValueIndex(enumValueNode, outputDirectory);
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

                outputLog.Append(". found root node: " + kvp.Key.name + '\n');
            }
        }

        while(rootNodes.Count > 0)
        {
            DefinitionNode node = rootNodes[0];
            rootNodes.RemoveAt(0);

            outputLog.Append(". new namespace section: " + node.name + '\n');

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
                IDefinition definition = definitionNodeMap[childNode];
                if(definition.protection == ProtectionLevel.Private)
                {
                    continue;
                }
                else if(definition is NamespaceDefinition)
                {
                    rootNodes.Insert(0, childNode);
                    outputLog.Append(". found inner namespace node: " + childNode.name + '\n');
                }
                else
                {
                    lines.Add("| " + GeneratePageLink(childNode)
                              + " | " + definitionNodeMap[childNode].briefDescription + " |");
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
        string fileName = GeneratePageName(classNode) + ".md";
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
        List<DefinitionNode> events = new List<DefinitionNode>();

        foreach(var childNode in classNode.children)
        {
            IDefinition childDef = definitionNodeMap[childNode];
            if(childDef.protection == ProtectionLevel.Private)
            {
                outputLog.Append(". ignoring private child: " + childNode.name + '\n');
                continue;
            }

            outputLog.Append(". processing " + childNode.name + '\n');

            MemberDefinition memberDef = childDef as MemberDefinition;
            if(memberDef is PropertyDefinition
               || memberDef is VariableDefinition)
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
            else if(memberDef is EventDefinition)
            {
                events.Add(childNode);
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

        // - desc -
        if(!String.IsNullOrEmpty(classDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(classDefinition.briefDescription);

            if(!String.IsNullOrEmpty(classDefinition.remarks))
            {
                lines.Add(classDefinition.remarks + '\n');
            }
        }

        // - members -
        if(const_props.Count > 0)
        {
            lines.Add("\n## Constant Properties");
            lines.AddRange(GenerateMemberTable(const_props));
        }

        if(static_props.Count > 0)
        {
            lines.Add("\n## Static Properties");
            lines.AddRange(GenerateMemberTable(static_props));
        }

        if(props.Count > 0)
        {
            lines.Add("\n## Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        if(const_methods.Count > 0)
        {
            lines.Add("\n## Constant Methods");
            lines.AddRange(GenerateMemberTable(const_methods));
        }

        if(static_methods.Count > 0)
        {
            lines.Add("\n## Static Methods");
            lines.AddRange(GenerateMemberTable(static_methods));
        }

        if(methods.Count > 0)
        {
            lines.Add("\n## Public Methods");
            lines.AddRange(GenerateMemberTable(methods));
        }

        if(events.Count > 0)
        {
            lines.Add("\n## Events");
            lines.AddRange(GenerateMemberTable(events));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateStructIndex(DefinitionNode structNode,
                                          string outputDirectory)
    {
        string fileName = GeneratePageName(structNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Struct Index: " + filePath + '\n');
        outputLog.Append(". id [" + structNode.d_id + "]\n");

        // - collect data -
        StructDefinition structDefinition = definitionNodeMap[structNode] as StructDefinition;
        List<DefinitionNode> const_props = new List<DefinitionNode>();
        List<DefinitionNode> static_props = new List<DefinitionNode>();
        List<DefinitionNode> props = new List<DefinitionNode>();
        List<DefinitionNode> const_methods = new List<DefinitionNode>();
        List<DefinitionNode> static_methods = new List<DefinitionNode>();
        List<DefinitionNode> methods = new List<DefinitionNode>();
        List<DefinitionNode> events = new List<DefinitionNode>();

        foreach(var childNode in structNode.children)
        {
            IDefinition childDef = definitionNodeMap[childNode];
            if(childDef.protection == ProtectionLevel.Private)
            {
                outputLog.Append(". ignoring private child: " + childNode.name + '\n');
                continue;
            }

            outputLog.Append(". processing " + childNode.name + '\n');

            MemberDefinition memberDef = childDef as MemberDefinition;
            if(memberDef is PropertyDefinition
               || memberDef is VariableDefinition)
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
            else if(memberDef is EventDefinition)
            {
                events.Add(childNode);
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

        lines.Add("# " + structNode.name + "\n");
        lines.Add("struct in " + DefinitionNode.GenerateFullName(structNode.parent)
                  + '\n');

        if(structDefinition.baseType != null)
        {
            lines.Add("inherits from: " + GenerateTypeMDString(structDefinition.baseType) + '\n');
        }

        if(structDefinition.interfaces.Count > 0)
        {
            StringBuilder interfaceString = new StringBuilder();
            interfaceString.Append("implements interfaces: ");

            foreach(TypeDescription interfaceType in structDefinition.interfaces)
            {
                interfaceString.Append(GenerateTypeMDString(interfaceType) + ", ");
            }

            interfaceString.Length -= 2;
            interfaceString.Append('\n');

            lines.Add(interfaceString.ToString());
        }

        // - desc -
        if(!String.IsNullOrEmpty(structDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(structDefinition.briefDescription);

            if(!String.IsNullOrEmpty(structDefinition.remarks))
            {
                lines.Add(structDefinition.remarks + '\n');
            }
        }

        // - members -
        if(const_props.Count > 0)
        {
            lines.Add("\n## Constant Properties");
            lines.AddRange(GenerateMemberTable(const_props));
        }

        if(static_props.Count > 0)
        {
            lines.Add("\n## Static Properties");
            lines.AddRange(GenerateMemberTable(static_props));
        }

        if(props.Count > 0)
        {
            lines.Add("\n## Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        if(const_methods.Count > 0)
        {
            lines.Add("\n## Constant Methods");
            lines.AddRange(GenerateMemberTable(const_methods));
        }

        if(static_methods.Count > 0)
        {
            lines.Add("\n## Static Methods");
            lines.AddRange(GenerateMemberTable(static_methods));
        }

        if(methods.Count > 0)
        {
            lines.Add("\n## Public Methods");
            lines.AddRange(GenerateMemberTable(methods));
        }

        if(events.Count > 0)
        {
            lines.Add("\n## Events");
            lines.AddRange(GenerateMemberTable(events));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateInterfaceIndex(DefinitionNode interfaceNode,
                                             string outputDirectory)
    {
        string fileName = GeneratePageName(interfaceNode) + ".md";
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

        // - desc -
        if(!String.IsNullOrEmpty(interfaceDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(interfaceDefinition.briefDescription);

            if(!String.IsNullOrEmpty(interfaceDefinition.remarks))
            {
                lines.Add(interfaceDefinition.remarks + '\n');
            }
        }

        // - members -
        if(props.Count > 0)
        {
            lines.Add("\n## Properties");
            lines.AddRange(GenerateMemberTable(props));
        }

        if(methods.Count > 0)
        {
            lines.Add("\n## Methods");
            lines.AddRange(GenerateMemberTable(methods));
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateEnumIndex(DefinitionNode enumNode,
                                        string outputDirectory)
    {
        string fileName = GeneratePageName(enumNode) + ".md";
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
            lines.Add(enumDefinition.briefDescription + '\n');

            if(!String.IsNullOrEmpty(enumDefinition.remarks))
            {
                lines.Add(enumDefinition.remarks + '\n');
            }
        }

        if(props.Count > 0)
        {
            lines.Add("\n## Properties");
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
            retVal.Add("| " + GeneratePageLink(memberNode)
                       + " | " + definitionNodeMap[memberNode].briefDescription + " |");
        }

        return retVal;
    }

    private static void CreateVariableIndex(DefinitionNode variableNode,
                                            string outputDirectory)
    {
        string fileName = GeneratePageName(variableNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Variable Index: " + filePath + '\n');

        VariableDefinition variableDefinition = definitionNodeMap[variableNode] as VariableDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# " + GeneratePageLink(variableNode.parent)
                  + '.' + variableNode.name + '\n');

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

            if(!String.IsNullOrEmpty(variableDefinition.remarks))
            {
                lines.Add(variableDefinition.remarks + '\n');
            }
        }

        // - initialized value -
        if(!String.IsNullOrEmpty(variableDefinition.initializedValue))
        {
            lines.Add("## Initialized Value\n");
            lines.Add("```c#");
            lines.Add(variableDefinition.initializedValue);
            lines.Add("```\n");
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateEventIndex(DefinitionNode eventNode,
                                         string outputDirectory)
    {
        string fileName = GeneratePageName(eventNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Event Index: " + filePath + '\n');

        EventDefinition eventDefinition = definitionNodeMap[eventNode] as EventDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# " + GeneratePageLink(eventNode.parent)
                  + '.' + eventNode.name + '\n');

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

            if(!String.IsNullOrEmpty(eventDefinition.remarks))
            {
                lines.Add(eventDefinition.remarks + '\n');
            }
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreatePropertyIndex(DefinitionNode propertyNode,
                                            string outputDirectory)
    {
        string fileName = GeneratePageName(propertyNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Property Index: " + filePath + '\n');

        PropertyDefinition propertyDefinition = definitionNodeMap[propertyNode] as PropertyDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# " + GeneratePageLink(propertyNode.parent)
                  + '.' + propertyNode.name + '\n');

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

            if(!String.IsNullOrEmpty(propertyDefinition.remarks))
            {
                lines.Add(propertyDefinition.remarks + '\n');
            }
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateEnumValueIndex(DefinitionNode valueNode,
                                             string outputDirectory)
    {
        string fileName = GeneratePageName(valueNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Enum Value Index: " + filePath + '\n');

        EnumValueDefinition valueDefinition = definitionNodeMap[valueNode] as EnumValueDefinition;
        List<string> lines = new List<string>();

        // - title -
        lines.Add("# " + GeneratePageLink(valueNode.parent)
                  + '.' + valueNode.name + '\n');

        // - description -
        if(!String.IsNullOrEmpty(valueDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(valueDefinition.briefDescription + '\n');

            if(!String.IsNullOrEmpty(valueDefinition.remarks))
            {
                lines.Add(valueDefinition.remarks + '\n');
            }
        }

        // - initialized value -
        if(!String.IsNullOrEmpty(valueDefinition.initializedValue))
        {
            lines.Add("## Initialized Value\n");
            lines.Add("```c#");
            lines.Add(valueDefinition.initializedValue);
            lines.Add("```\n");
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

        string fileName = GeneratePageName(functionNode) + ".md";
        string filePath = outputDirectory + fileName;

        outputLog.Append("\n--------------------\nCreating Function Index: " + filePath + '\n');

        FunctionDefinition functionDefinition = definitionNodeMap[functionNode] as FunctionDefinition;
        List<string> lines = new List<string>();
        StringBuilder buildString = null;

        // - title -
        lines.Add("# " + GeneratePageLink(functionNode.parent)
                  + '.' + functionNode.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(functionDefinition.protection.ToString().ToLower() + " ");

        if(functionDefinition.isStatic)
        {
            buildString.Append("static ");
        }

        if(functionDefinition.type != null)
        {
            // TODO(@jackson): Mark constructor separately?
            buildString.Append(GenerateTypeMDString(functionDefinition.type) + ' ');
        }

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
        if(functionDefinition.type != null
           && functionDefinition.type.name != "void")
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

            if(!String.IsNullOrEmpty(functionDefinition.remarks))
            {
                lines.Add(functionDefinition.remarks + '\n');
            }
        }

        // - example -

        // - seealso -

        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static string GenerateTypeMDString(TypeDescription typeDesc)
    {
        if(typeDesc == null) { return string.Empty; }

        StringBuilder sb = new StringBuilder();

        if(typeDesc.isRef)
        {
            outputLog.Append(". resolving type ref id [" + typeDesc.d_id + "]\n");

            DefinitionNode typeNode = FindDefinitionNodeById(typeDesc.d_id);
            sb.Append("[" + typeNode.name + "](" + GeneratePageName(typeNode) + ")");
        }
        else
        {
            sb.Append(typeDesc.name);
        }

        if(typeDesc.templatedTypes != null)
        {
            sb.Append("\\<");

            foreach(TypeDescription innerDesc in typeDesc.templatedTypes)
            {
                sb.Append(GenerateTypeMDString(innerDesc) + ',');
            }

            sb.Length -= 1;

            sb.Append('>');
        }

        if(typeDesc.isArray)
        {
            sb.Append("[]");
        }

        return sb.ToString();
    }

    private static string GeneratePageName(DefinitionNode node)
    {
        return DefinitionNode.GenerateFullName(node).Replace("<", "[").Replace(">", "]");
    }

    private static string GeneratePageLink(DefinitionNode node)
    {
        return("[" + node.name.Replace("<", "\\<") + "](" + GeneratePageName(node) + ")");
    }
}
