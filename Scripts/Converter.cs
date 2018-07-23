using System;
using System.Text;
using System.Xml;
using System.Collections.Generic;
using System.IO;

// TASKS
// . enums (in namespace files, memberdefs, children of namespace compounddef)
// . inherited member parent nodes (definition?)
// . inner classes (DONE?)
// . remarks is in detailedDescription!
// . add GitHub links
// . layout/titling work

public enum WikiMode
{
    Unity = 0,
}

public static class Converter
{
    private delegate void ProcessNode(XmlNode xmlNode, DefinitionNode parentNode);

    // AS definitionNodeMap
    private static Dictionary<DefinitionNode, IDefinition> definitionNodeMap;
    private static List<DefinitionNode> rootNodes;

    private static StringBuilder outputLog;

    private static Exception lastRunException = null;


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
        rootNodes = new List<DefinitionNode>();
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

    private static IEnumerable<DefinitionNode> FindDefinitionNodesWithName(string name)
    {
        foreach(DefinitionNode node in definitionNodeMap.Keys)
        {
            if(node.name == name) { yield return node; }
        }
    }

    private static DefinitionNode FindDefinitionNodeAt(string nodeHierarchy)
    {
        string[] nodeNames = nodeHierarchy.Split('.');
        int lastNodeNameIndex = nodeNames.Length - 1;
        DefinitionNode foundNode = null;

        foreach(DefinitionNode node in rootNodes)
        {
            if(node.name == nodeNames[0])
            {
                foundNode = node;
                break;
            }
        }

        int childIndex = 0;
        int currentDepth = 1;

        while(foundNode != null
              && currentDepth < lastNodeNameIndex)
        {
            DefinitionNode childNode = foundNode.children[childIndex];

            if(childNode.name == nodeNames[currentDepth])
            {
                foundNode = childNode;
                ++currentDepth;
                childIndex = 0;
            }
            else
            {
                ++childIndex;
            }

            if(childIndex >= foundNode.children.Count)
            {
                foundNode = null;
            }
        }

        return foundNode;
    }

    private static DefinitionNode CreateOrGetNodeAt(string nodeHierarchy)
    {
        outputLog.Append(". node hierachy: ");

        string[] nodeNames = nodeHierarchy.Split('.');
        DefinitionNode lastNode = null;

        foreach(DefinitionNode node in rootNodes)
        {
            if(node.name == nodeNames[0])
            {
                lastNode = node;
                break;
            }
        }

        if(lastNode == null)
        {
            lastNode = new DefinitionNode();
            lastNode.name = nodeNames[0];

            definitionNodeMap[lastNode] = null;

            rootNodes.Add(lastNode);

            outputLog.Append("[NEW]");
        }

        outputLog.Append(nodeNames[0] + '.');

        int childIndex = 0;
        int currentDepth = 1;

        while(currentDepth < nodeNames.Length)
        {
            if(childIndex >= lastNode.children.Count)
            {
                DefinitionNode newChild = new DefinitionNode();
                newChild.name = nodeNames[currentDepth];

                definitionNodeMap[newChild] = null;

                lastNode.children.Add(newChild);
                newChild.parent = lastNode;

                outputLog.Append("[NEW]");
            }

            DefinitionNode lastNodeChild = lastNode.children[childIndex];

            if(lastNodeChild.name == nodeNames[currentDepth])
            {
                outputLog.Append(nodeNames[currentDepth] + '.');

                lastNode = lastNodeChild;
                ++currentDepth;
                childIndex = 0;
            }
            else
            {
                ++childIndex;
            }
        }

        outputLog.Length -= 1;
        outputLog.Append('\n');

        return lastNode;
    }

    private static string ParseXMLString(string xmlString)
    {
        return xmlString.Replace("&lt; ", "[").Replace("&lt;", "[")
                        .Replace(" &gt;", "]").Replace("&gt;", "]")
                        .Replace("&apos;","\'");
    }

    private static string ParseDescription(XmlNode descriptionNode)
    {
        Stack<XmlNode> nodeStack = new Stack<XmlNode>();
        nodeStack.Push(descriptionNode);

        StringBuilder retVal = new StringBuilder();
        ProcessDescriptionNode(descriptionNode, retVal);

        return retVal.ToString();
    }

    private static void ProcessDescriptionNode(XmlNode node, StringBuilder result)
    {
        bool isTagUnrecognised = false;

        // - open tag -
        switch(node.Name)
        {
            case "briefdescription":
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
                ProcessDescriptionNode(childNode, result);
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
                parentDesc.templatedTypes.AddRange(ProcessTypeNode(childNode));
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


    // --------- [PROCESSING] ---------
    // private static Dictionary<string, Action<XmlNode, IDefinition>> nodeActionMap = new Dictionary<string, Action<XmlNode, IDefinition>>
    private static Dictionary<string, ProcessNode> nodeActionMap = new Dictionary<string, ProcessNode>
    {
        { "compounddef", Converter.ProcessCompound },
        { "innerclass", Converter.ProcessInnerClass },
        { "sectiondef", Converter.ProcessSection },
        { "memberdef", Converter.ProcessMember },
        { "type", Converter.ProcessType },
        { "location", Converter.ProcessLocation },
        // { "briefdescription", Converter.ProcessBriefDescription },
        { "detaileddescription", Converter.ProcessDetailedDescription },
    };

    private static List<string> nodesToSkip = new List<string>()
    {
        "listofallmembers",
        "compoundname",
        "name",
        "briefdescription",
    };

    private static void ProcessChildNodes(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append("Processing children of " + xmlNode.Name + " for parent " + (parentNode == null ? "NULL" : parentNode.name) + '\n');

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
                ProcessChildNodes(xmlChildNode, parentNode);
            }
        }
    }

    private static void ProcessCompound(XmlNode xmlNode, DefinitionNode parentNode)
    {
        switch(xmlNode.Attributes["kind"].Value)
        {
            case "file":
            {
                outputLog.Append(". is an ignored compound kind (" + xmlNode.Attributes["kind"].Value + ")\n");
            }
            break;

            case "class":
            {
                outputLog.Append(". is class\n");
                ProcessClass(xmlNode, parentNode);
            }
            break;

            case "interface":
            {
                outputLog.Append(". is interface\n");
                ProcessInterface(xmlNode, parentNode);
            }
            break;

            default:
            {
                outputLog.Append(". is unknown compound kind: \"" + xmlNode.Attributes["kind"].Value + "\"\n");
                ProcessChildNodes(xmlNode, parentNode);
            }
            break;
        }
    }

    private static void ProcessClass(XmlNode xmlNode, DefinitionNode parentNode)
    {
        // - get node -
        string nodeId = xmlNode.Attributes["id"].Value;
        DefinitionNode classNode = FindDefinitionNodeById(nodeId);

        if(classNode == null)
        {
            outputLog.Append(". new class node[" + nodeId + "]\n");
            string nodeHierarchy = xmlNode["compoundname"].InnerXml.Replace("::", ".");
            classNode = CreateOrGetNodeAt(nodeHierarchy);
            classNode.d_id = nodeId;
        }

        // - get def -
        CompoundDefinition classDef = definitionNodeMap[classNode] as CompoundDefinition;

        if(classDef == null)
        {
            classDef = new CompoundDefinition();
            classDef.name = classNode.name;

            definitionNodeMap[classNode] = classDef;

            outputLog.Append(". new class definition\n");
        }

        // - attributes -
        classDef.kind = xmlNode.Attributes["kind"].Value; // "class", "struct", etc

        // - instantiate member nodes -
        outputLog.Append(". processing member list\n");
        foreach(XmlNode memberNode in xmlNode["listofallmembers"].ChildNodes)
        {
            // outputLog.Append(". member node: " + memberNode.name);

            string memberId = memberNode.Attributes["refid"].Value;
            string memberFullName = (DefinitionNode.GenerateFullName(classNode)
                                     + '.' + ParseXMLString(memberNode["name"].InnerXml).Replace("::", "."));

            DefinitionNode memberDefNode = FindDefinitionNodeById(memberId);
            if(memberDefNode == null)
            {
                outputLog.Append(". instantiating member node [" + memberId + "]\n");
                memberDefNode = CreateOrGetNodeAt(memberFullName);
                memberDefNode.d_id = memberId;
            }
            else
            {
                outputLog.Append(". existing member node found [" + memberId + "]: "
                                 + DefinitionNode.GenerateFullName(memberDefNode) + '\n');
            }
        }

        // - Process Members -
        ProcessChildNodes(xmlNode, classNode);
    }

    private static void ProcessInterface(XmlNode xmlNode, DefinitionNode parentNode)
    {
        // - get node -
        string nodeId = xmlNode.Attributes["id"].Value;
        DefinitionNode interfaceNode = FindDefinitionNodeById(nodeId);

        if(interfaceNode == null)
        {
            outputLog.Append(". new interface node[" + nodeId + "]\n");
            string nodeHierarchy = xmlNode["compoundname"].InnerXml.Replace("::", ".");
            interfaceNode = CreateOrGetNodeAt(nodeHierarchy);
            interfaceNode.d_id = nodeId;
        }

        // - get def -
        CompoundDefinition interfaceDefinition = definitionNodeMap[interfaceNode] as CompoundDefinition;

        if(interfaceDefinition == null)
        {
            interfaceDefinition = new CompoundDefinition();
            interfaceDefinition.name = interfaceNode.name;

            definitionNodeMap[interfaceNode] = interfaceDefinition;

            outputLog.Append(". new interface definition\n");
        }

        // - attributes -
        interfaceDefinition.kind = xmlNode.Attributes["kind"].Value; // "class", "struct", etc

        // - instantiate member nodes -
        outputLog.Append(". processing member list\n");
        foreach(XmlNode memberNode in xmlNode["listofallmembers"].ChildNodes)
        {
            // outputLog.Append(". member node: " + memberNode.name);

            string memberId = memberNode.Attributes["refid"].Value;
            string memberFullName = (DefinitionNode.GenerateFullName(interfaceNode)
                                     + '.' + ParseXMLString(memberNode["name"].InnerXml).Replace("::", "."));

            DefinitionNode memberDefNode = FindDefinitionNodeById(memberId);
            if(memberDefNode == null)
            {
                outputLog.Append(". instantiating member node [" + memberId + "]\n");
                memberDefNode = CreateOrGetNodeAt(memberFullName);
                memberDefNode.d_id = memberId;
            }
            else
            {
                outputLog.Append(". existing member node found [" + memberId + "]: "
                                 + DefinitionNode.GenerateFullName(memberDefNode) + '\n');
            }
        }

        // - Process Members -
        ProcessChildNodes(xmlNode, interfaceNode);
    }

    private static void ProcessInnerClass(XmlNode xmlNode, DefinitionNode parentNode)
    {
        // - assert node exists -
        string nodeId = xmlNode.Attributes["refid"].Value;
        DefinitionNode classNode = FindDefinitionNodeById(nodeId);

        if(classNode == null)
        {
            outputLog.Append(". new class node [" + nodeId + "]\n");
            string nodeHierarchy = xmlNode.InnerText.Replace("::", ".");
            classNode = CreateOrGetNodeAt(nodeHierarchy);

            classNode.d_id = nodeId;
        }
        else
        {
            outputLog.Append(". inner class exists: " + DefinitionNode.GenerateFullName(classNode));
        }
    }

    private static void ProcessSection(XmlNode node, DefinitionNode parentNode)
    {
        outputLog.Append(". kind: " + node.Attributes["kind"].Value + '\n');
        ProcessChildNodes(node, parentNode);
    }

    private static void ProcessMember(XmlNode xmlNode, DefinitionNode parentNode)
    {
        // - get name -
        string memberName = ParseXMLString(xmlNode["name"].InnerXml).Replace("::", ".");
        outputLog.Append(". name: " + memberName + '\n');

        // - get node -
        string nodeId = xmlNode.Attributes["id"].Value;
        DefinitionNode memberNode = FindDefinitionNodeById(nodeId);

        if(memberNode == null)
        {
            outputLog.Append(". not found by id [" + nodeId + "]\n");

            memberNode = CreateOrGetNodeAt(DefinitionNode.GenerateFullName(parentNode)
                                           + '.' + memberName);
            memberNode.d_id = nodeId;
        }
        else if(memberNode.parent != parentNode)
        {
            outputLog.Append("WARNING: memberNode.parent [" + (memberNode.parent == null ? "NULL"
                             : DefinitionNode.GenerateFullName(memberNode.parent)) + "] != parentNode ["
                             + (parentNode == null ? "NULL" : DefinitionNode.GenerateFullName(parentNode))
                             + "]\n");
        }

        // - get def -
        MemberDefinition memberDef = definitionNodeMap[memberNode] as MemberDefinition;

        if(memberDef == null)
        {
            memberDef = new MemberDefinition();
            memberDef.name = memberName;

            definitionNodeMap[memberNode] = memberDef;

            outputLog.Append(". new member definition\n");
        }

        // - build def -
        string memberType = xmlNode["type"].InnerText;
        if(memberType.Contains("const "))
        {
            memberType.Replace("const ", "");
            memberDef.isConst = true;
            memberDef.isStatic = (mode == WikiMode.Unity);
        }

        if(memberType.Contains("readonly "))
        {
            memberType.Replace("readonly ", "");
            memberDef.isConst = true;
        }

        memberDef.type = ParseTypeDescription(xmlNode["type"]);

        switch(xmlNode.Attributes["kind"].Value)
        {
            case "variable":
            {
                memberDef.kind = MemberKind.Variable;
            }
            break;
            case "function":
            {
                if(memberDef.type == null)
                {
                    memberDef.kind = MemberKind.Constructor;
                }
                else
                {
                    memberDef.kind = MemberKind.Method;
                }
            }
            break;
            case "event":
            {
                memberDef.kind = MemberKind.Event;
            }
            break;
            case "property":
            {
                memberDef.kind = MemberKind.Property;
            }
            break;
            default:
            {
                outputLog.Append("WARNING: Unrecognized member kind ("
                                 + xmlNode.Attributes["kind"].Value + "). Skipping.\n");
            }
            return;
        }

        switch(xmlNode.Attributes["prot"].Value)
        {
            case "public":
            {
                memberDef.protection = ProtectionLevel.Public;
            }
            break;
            case "private":
            {
                memberDef.protection = ProtectionLevel.Private;
            }
            break;
            case "protected":
            {
                memberDef.protection = ProtectionLevel.Protected;
            }
            break;
        }

        memberDef.isStatic = (xmlNode.Attributes["static"].Value == "yes");

        memberDef.briefDescription = ParseDescription(xmlNode["briefdescription"]).Replace("\n\n", "");
    }

    private static void ProcessType(XmlNode node, DefinitionNode parentNode)
    {
        outputLog.Append(". not implemented\n");
    }

    private static void ProcessLocation(XmlNode node, DefinitionNode parentNode)
    {
        outputLog.Append(". not implemented\n");
    }

    private static void ProcessDetailedDescription(XmlNode xmlNode, DefinitionNode parentNode)
    {
        outputLog.Append(". not implemented\n");
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
        List<DefinitionNode> memberNodes = new List<DefinitionNode>();

        foreach(var kvp in definitionNodeMap)
        {
            if(kvp.Value is CompoundDefinition)
            {
                classNodes.Add(kvp.Key);
            }
            else if(kvp.Value is MemberDefinition)
            {
                memberNodes.Add(kvp.Key);
            }
        }

        // - output indicies -
        foreach(DefinitionNode classNode in classNodes)
        {
            CreateClassIndex(classNode, outputDirectory);
        }

        foreach(DefinitionNode memberNode in memberNodes)
        {
            CreateMemberIndex(memberNode, outputDirectory);
        }

        outputLog.Append("END BUILDING WIKI\n");
    }

    private static void CreateAPIIndex(string outputDirectory)
    {
        outputLog.Append("Creating API Index\n");

        List<string> lines = new List<string>(definitionNodeMap.Count + 20);
        List<DefinitionNode> namespaceNodes = new List<DefinitionNode>(rootNodes.Count);

        foreach(DefinitionNode node in rootNodes)
        {
            if(definitionNodeMap[node] == null)
            {
                outputLog.Append(". adding root node: " + node.name);
                namespaceNodes.Add(node);
            }
        }

        while(namespaceNodes.Count > 0)
        {
            DefinitionNode node = namespaceNodes[0];
            namespaceNodes.RemoveAt(0);

            outputLog.Append(". new namespace section: " + node.name);


            StringBuilder fullNamespace = new StringBuilder();
            DefinitionNode nameNode = node;
            while(nameNode != null)
            {
                fullNamespace.Insert(0, nameNode.name + ".");
                nameNode = nameNode.parent;
            }
            fullNamespace.Length -= 1;

            lines.Add("## Namespace: " + fullNamespace.ToString() + '\n');

            lines.Add("| Class | Description |");
            lines.Add("| - | - |");

            foreach(var childNode in node.children)
            {
                outputLog.Append(". processing node: " + childNode.name);

                if(definitionNodeMap[childNode] == null)
                {
                    namespaceNodes.Insert(0, childNode);
                }
                else
                {
                    lines.Add("| [" + definitionNodeMap[childNode].name + "]("
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

        outputLog.Append("Creating Class Index: " + filePath + '\n');

        // - collect data -
        CompoundDefinition classDefinition = definitionNodeMap[classNode] as CompoundDefinition;
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

            if(memberDef == null)// && definitionNodeMap[childNode].protection == ProtectionLevel.Private)
            {
                outputLog.Append("WARNING: non-member child. Skipping.\n");
                continue;
            }
            else
            {
                switch(memberDef.kind)
                {
                    case MemberKind.Variable:
                    case MemberKind.Property:
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
                    break;
                    case MemberKind.Method:
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
                    break;
                    default:
                    {
                        outputLog.Append("WARNING: " + memberDef.name
                                         + " has unrecognized member kind (" + memberDef.kind.ToString()
                                         + "). Ignoring.\n");
                    }
                    break;
                }
            }
        }

        // - build index -
        List<string> lines = new List<string>();
        bool isStaticClass = (props.Count == 0 && methods.Count == 0);

        lines.Add("# " + classDefinition.name + "\n");
        lines.Add((isStaticClass ? "static " : "")
                  + classDefinition.kind.ToLower()
                  + " in " + DefinitionNode.GenerateFullName(classNode.parent));

        if(!String.IsNullOrEmpty(classDefinition.briefDescription))
        {
            lines.Add("## Description\n");
            lines.Add(classDefinition.briefDescription);
        }

        if(const_props.Count > 0)
        {
            lines.Add("\n# Constant Properties");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in const_props)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        if(static_props.Count > 0)
        {
            lines.Add("\n# Static Properties");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in static_props)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        if(props.Count > 0)
        {
            lines.Add("\n# Properties");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in props)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        if(const_methods.Count > 0)
        {
            lines.Add("\n# Constant Methods");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in const_methods)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        if(static_methods.Count > 0)
        {
            lines.Add("\n# Static Methods");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in static_methods)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        if(methods.Count > 0)
        {
            lines.Add("\n# Public Methods");
            lines.Add("| Name | Description |");
            lines.Add("| - | - |");

            foreach(var memberNode in methods)
            {
                lines.Add(GenerateClassIndexMemberEntry(memberNode));
            }
        }

        // - write file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static void CreateMemberIndex(DefinitionNode memberNode,
                                          string outputDirectory)
    {
        // - Static Member Layout -
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

        MemberDefinition memberDefinition = definitionNodeMap[memberNode] as MemberDefinition;

        string fileName = DefinitionNode.GenerateFullName(memberNode) + ".md";
        string filePath = outputDirectory + fileName;
        StringBuilder buildString = null;

        outputLog.Append("Creating Member Index: " + filePath + '\n');

        List<string> lines = new List<string>();

        lines.Add("# [" + memberNode.parent.name + "](" + DefinitionNode.GenerateFullName(memberNode.parent)
                  + ")." + memberDefinition.name + '\n');

        // - definition -
        buildString = new StringBuilder();
        buildString.Append(memberDefinition.protection.ToString().ToLower() + " ");

        if(memberDefinition.type != null)
        {
            buildString.Append(GenerateTypeMDString(memberDefinition.type) + ' ');
        }

        buildString.Append(memberDefinition.name + ";\n");
        lines.Add(buildString.ToString());

        // - parameters -

        // - description -
        if(!String.IsNullOrEmpty(memberDefinition.briefDescription)
           || !String.IsNullOrEmpty(memberDefinition.detailedDescription))
        {
            lines.Add("## Description\n");

            if(!String.IsNullOrEmpty(memberDefinition.briefDescription))
            {
                lines.Add(memberDefinition.briefDescription + '\n');
            }
            if(!String.IsNullOrEmpty(memberDefinition.detailedDescription))
            {
                lines.Add(memberDefinition.detailedDescription + '\n');
            }
        }

        // - example -

        // - seealso -


        // - create file -
        File.WriteAllLines(filePath, lines.ToArray());
    }

    private static string GenerateClassIndexMemberEntry(DefinitionNode node)
    {
        MemberDefinition memberDef = definitionNodeMap[node] as MemberDefinition;
        return("| [" + memberDef.name + "](" + DefinitionNode.GenerateFullName(node) + ") "
               + "| " + memberDef.briefDescription + " |");
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
