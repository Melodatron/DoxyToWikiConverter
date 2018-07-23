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
    string d_id { get; set; }
    string name { get; set; }
    string briefDescription { get; set; }
    string detailedDescription { get; set; }
    ProtectionLevel protection { get; set; }
}

public class CompoundDefinition : IDefinition
{
    public string d_id { get; set; }
    public string name { get; set; }
    public string briefDescription { get; set; }
    public string detailedDescription { get; set; }
    public ProtectionLevel protection { get; set; }

    public string kind = string.Empty;
}

public enum MemberKind
{
    _UNKNOWN_ = 0,
    Variable,
    Method,
    Event,
    Property,
    Constructor,
}

public class MemberDefinition : IDefinition
{
    public string d_id { get; set; }
    public string name { get; set; }
    public string briefDescription { get; set; }
    public string detailedDescription { get; set; }
    public ProtectionLevel protection { get; set; }

    public MemberKind kind = MemberKind._UNKNOWN_;
    public TypeDescription type = new TypeDescription();

    public bool isConst = false;
    public bool isStatic = false;

    // - function bits -
    public List<FunctionParameter> parameters = null;
}

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
