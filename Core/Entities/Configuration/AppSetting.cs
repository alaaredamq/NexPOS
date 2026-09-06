using Core.Entities.Base;

namespace Core.Entities.Configuration;

public class AppSetting : IEntityBase<int>
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Value { get; set; }
    public string Description { get; set; }
    public string Type { get; set; } // e.g., "string", "int", "bool"
    
    public dynamic GetTypedValue()
    {
        return Type.ToLower() switch
        {
            "int" => int.TryParse(Value, out var intValue) ? intValue : 0,
            "bool" => bool.TryParse(Value, out var boolValue) ? boolValue : false,
            "float" or "double" => double.TryParse(Value, out var doubleValue) ? doubleValue : 0.0,
            "datetime" => DateTime.TryParse(Value, out var dateTimeValue) ? dateTimeValue : DateTime.MinValue,
            _ => Value // Default to string
        };
    }
    
    public AppSetting(string name, string value, string description, string type)
    {
        Name = name;
        Value = value;
        Description = description;
        Type = type;
    }
    
    public override string ToString()
    {
        return $"{Name} ({Type}): {Value} - {Description}";
    }
}