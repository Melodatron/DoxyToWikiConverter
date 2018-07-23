using System.Collections.Generic;

public class DefinitionNode
{
    public string d_id = string.Empty;
    public string name = string.Empty;

    public DefinitionNode parent = null;
    public List<DefinitionNode> children = new List<DefinitionNode>(0);

    public static string GenerateFullName(DefinitionNode node)
    {
        var fullName = new System.Text.StringBuilder();

        while(node != null)
        {
            fullName.Insert(0, node.name + '.');
            node = node.parent;
        }

        fullName.Length -= 1;

        return fullName.ToString();
    }
}
