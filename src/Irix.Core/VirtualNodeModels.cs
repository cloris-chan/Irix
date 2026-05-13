namespace Irix;

public enum VirtualNodeKind
{
    Text,
    Rectangle,
    Button,
    ScrollContainer
}

public enum VirtualNodePatchOperation
{
    ReplaceRoot,
    Add,
    Remove,
    Update,
    Move
}

public enum AttributeValueKind
{
    None,
    Text,
    Number,
    Boolean,
    ActionId
}

public enum NodeContentKind
{
    None,
    Text,
    Number,
    Boolean
}

public readonly record struct VirtualNodeTree(VirtualNode Root);

public readonly record struct VirtualNode
{
    public VirtualNode(
        VirtualNodeKind kind,
        NodeKey key = default,
        NodeContent content = default,
        VirtualNodeAttribute[]? attributes = null,
        VirtualNode[]? children = null)
    {
        Kind = kind;
        Key = key;
        Content = content;
        Attributes = attributes ?? [];
        Children = children ?? [];
    }

    public VirtualNodeKind Kind { get; }

    public NodeKey Key { get; }

    public NodeContent Content { get; }

    public VirtualNodeAttribute[] Attributes { get; }

    public VirtualNode[] Children { get; }
}

public readonly record struct VirtualNodeAttribute(VirtualAttributeKey Key, AttributeValue Value)
{
    public string Name => Key.ToString();

    public static VirtualNodeAttribute Action(ActionId actionId) =>
        new(VirtualAttributeKey.ActionId, AttributeValue.FromActionId(actionId));
}

public readonly record struct NodeContent(
    NodeContentKind Kind,
    string? Text = null,
    double Number = 0,
    bool Boolean = false)
{
    public static NodeContent None => default;

    public static NodeContent FromText(string value) => new(NodeContentKind.Text, value);

    public static NodeContent FromNumber(double value) => new(NodeContentKind.Number, Number: value);

    public static NodeContent FromBoolean(bool value) => new(NodeContentKind.Boolean, Boolean: value);
}

public readonly record struct AttributeValue(
    AttributeValueKind Kind,
    string? Text = null,
    double Number = 0,
    bool Boolean = false,
    ActionId ActionIdValue = default)
{
    public static AttributeValue None => default;

    public static AttributeValue FromText(string value) => new(AttributeValueKind.Text, value);

    public static AttributeValue FromNumber(double value) => new(AttributeValueKind.Number, Number: value);

    public static AttributeValue FromBoolean(bool value) => new(AttributeValueKind.Boolean, Boolean: value);

    public static AttributeValue FromActionId(ActionId value) => new(AttributeValueKind.ActionId, ActionIdValue: value);
}

public readonly record struct VirtualNodePatch(
    VirtualNodePatchOperation Operation,
    int NodeIndex,
    VirtualNode Node,
    int ScreenId = 0);

public static class VirtualNodeFactory
{
    public static VirtualNode Text(string content, uint key = 0, params VirtualNodeAttribute[] attributes) =>
        new(VirtualNodeKind.Text, new NodeKey(key), NodeContent.FromText(content), attributes);

    public static VirtualNode Rectangle(double width, double height, uint key = 0, params VirtualNodeAttribute[] attributes) =>
        new(
            VirtualNodeKind.Rectangle,
            new NodeKey(key),
            attributes:
            [
                .. attributes,
                new VirtualNodeAttribute(VirtualAttributeKey.Width, AttributeValue.FromNumber(width)),
                new VirtualNodeAttribute(VirtualAttributeKey.Height, AttributeValue.FromNumber(height))
            ]);

    public static VirtualNode Button(string label, uint key = 0, params VirtualNodeAttribute[] attributes) =>
        new(
            VirtualNodeKind.Button,
            new NodeKey(key),
            attributes: attributes,
            children: [Text(label, key + 1)]);

    public static VirtualNode ScrollContainer(uint key = 0, params VirtualNode[] children) =>
        new(VirtualNodeKind.ScrollContainer, new NodeKey(key), children: children);
}
