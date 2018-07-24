using System.Collections.Generic;

using Debug = UnityEngine.Debug;

public enum ProtectionLevel
{
    _UNKNOWN_ = 0,
    Public,
    Private,
    Protected
}

public interface IDefinition
{
    DefinitionNode node { get; set; }
    string briefDescription { get; set; }
    string detailedDescription { get; set; }
    ProtectionLevel protection { get; set; }
}

public class CompoundDefinition : IDefinition
{
    public DefinitionNode node { get; set; }
    public string briefDescription { get; set; }
    public string detailedDescription { get; set; }
    public ProtectionLevel protection { get; set; }
}

public class NamespaceDefinition : CompoundDefinition
{
    public string language = "unknown";
}

public class ObjectDefinition : CompoundDefinition
{
    public TypeDescription baseType = null;
    public List<TypeDescription> interfaces = new List<TypeDescription>(0);
}

public class ClassDefinition : ObjectDefinition {}
public class StructDefinition : ObjectDefinition {}
public class InterfaceDefinition : ObjectDefinition {}

public enum MemberKind
{
    _UNKNOWN_ = 0,
    Variable,
    Method,
    Event,
    Property,
    Constructor,
    Enum,
}

public abstract class MemberDefinition : IDefinition
{
    public DefinitionNode node { get; set; }
    public string briefDescription { get; set; }
    public string detailedDescription { get; set; }
    public ProtectionLevel protection { get; set; }

    public TypeDescription type = new TypeDescription();
    public bool isConst = false;
    public bool isStatic = false;
}

public class FunctionDefinition : MemberDefinition
{
    // - function bits -
    public List<FunctionParameter> parameters = null;
}

public class VariableDefinition     : MemberDefinition {}
public class PropertyDefinition     : MemberDefinition {}
public class EventDefinition        : MemberDefinition {}
public class EnumDefinition         : MemberDefinition {}
public class EnumValueDefinition    : MemberDefinition {}

public class TypeDescription
{
    public string name;
    public string d_id;
    public bool isRef;

    public List<TypeDescription> templatedTypes;
}

public class FunctionParameter
{
    public string name;
    public TypeDescription type;

    public string description;
}
