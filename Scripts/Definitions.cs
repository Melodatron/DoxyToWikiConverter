using System.Collections.Generic;

using Debug = UnityEngine.Debug;

public enum ProtectionLevel
{
    _UNKNOWN_ = 0,
    Public,
    Private,
    Protected,
    Internal
}

public interface IDefinition
{
    DefinitionNode node { get; set; }
    string briefDescription { get; set; }
    string remarks { get; set; }
    ProtectionLevel protection { get; set; }
}

public class CompoundDefinition : IDefinition
{
    public DefinitionNode node { get; set; }
    public string briefDescription { get; set; }
    public string remarks { get; set; }
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
    public string remarks { get; set; }
    public ProtectionLevel protection { get; set; }

    public TypeDescription type = new TypeDescription();
    public bool isConst = false;
    public bool isStatic = false;
    public string initializedValue = null;
}

public class FunctionDefinition : MemberDefinition
{
    public List<FunctionParameter> parameters = new List<FunctionParameter>();
    public string returnDescription = null;
}

public class PropertyDefinition     : MemberDefinition
{
    public ProtectionLevel getterProtection = ProtectionLevel._UNKNOWN_;
    public ProtectionLevel setterProtection = ProtectionLevel._UNKNOWN_;
}

public class VariableDefinition     : MemberDefinition {}
public class EventDefinition        : MemberDefinition {}
public class EnumDefinition         : MemberDefinition {}
public class EnumValueDefinition    : MemberDefinition {}

public class TypeDescription
{
    public string name = string.Empty;
    public string d_id = null;
    public bool isRef = false;
    public bool isArray = false;

    public List<TypeDescription> templatedTypes = null;
}

public class FunctionParameter
{
    public string name = string.Empty;
    public TypeDescription type = null;
    public string description = null;

    public bool isOutParam = false;
    public bool isRefParam = false;
}
