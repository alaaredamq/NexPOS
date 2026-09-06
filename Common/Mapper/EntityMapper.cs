using System.Collections;
using System.Reflection;

namespace Common.Mapper;

public static class EntityMapper
{
    public static MappingResult<T> Map<T>(object? entity) where T : class, new()
    {
        if (entity == null)
            return new MappingResult<T> { IsSuccessful = false, ErrorMessage = "Entity is empty" };

        try
        {
            T destination = new();
            Type sourceType = entity.GetType();
            Type destinationType = typeof(T);

            PropertyInfo[] sourceProperties = sourceType.GetProperties();
            PropertyInfo[] destinationProperties = destinationType.GetProperties();

            // if (sourceType == destinationType)
            //     return new MappingResult<T> { Result = entity as T, IsSuccessful = true };

            if (sourceType.IsGenericType && sourceType.GetGenericTypeDefinition() == typeof(List<>))
            {
                var sourceList = (IList)entity;
                var destinationList = (IList)Activator.CreateInstance(destinationType);

                // type of object of destinationList
                var type = destinationType.GetGenericArguments()[0];
                var mapMethod = typeof(EntityMapper)
                    .GetMethod(nameof(Map), BindingFlags.NonPublic | BindingFlags.Static)?
                    .MakeGenericMethod(type);

                foreach (var item in sourceList)
                {
                    var result = mapMethod.Invoke(null, new object[] { item, type });

                    // Dynamically cast the result to `MappingResult<>`
                    var castedResult =
                        Convert.ChangeType(result, typeof(MappingResult<>).MakeGenericType(type)) as dynamic;

                    if (castedResult != null && castedResult.IsSuccessful)
                    {
                        destinationList.Add(castedResult.Result);
                    }
                    else
                    {
                        return new MappingResult<T>
                        {
                            IsSuccessful = false,
                            ErrorMessage = castedResult?.ErrorMessage ?? "Unknown error during mapping"
                        };
                    }
                }

                return new MappingResult<T> { Result = destinationList as T, IsSuccessful = true };
            }

            foreach (var property in sourceProperties)
            {
                PropertyInfo destinationProperty = destinationProperties.FirstOrDefault(x =>
                    x.Name == property.Name);
                if (destinationProperty != null && destinationProperty.CanWrite && property.CanRead)
                {
                    if(destinationProperty.PropertyType != property.PropertyType)
                        return new MappingResult<T> { IsSuccessful = false, ErrorMessage = $"type mismatch for property '{property.Name}'" };
                    
                    destinationProperty.SetValue(destination, property.GetValue(entity));
                }
                else {
                    continue;
                }
            }

            return new MappingResult<T> { Result = destination, IsSuccessful = true };
        }
        catch (Exception e)
        {
            return new MappingResult<T> { IsSuccessful = false, ErrorMessage = e.StackTrace };
        }
    }

    private static MappingResult<T> Map<T>(object entity, Type destinationType) where T : class, new()
    {
        if (entity == null)
            return new MappingResult<T> { IsSuccessful = false, ErrorMessage = "Entity is empty" };

        try
        {
            T destination = new();
            Type sourceType = entity.GetType();

            PropertyInfo[] sourceProperties = sourceType.GetProperties();
            PropertyInfo[] destinationProperties = destinationType.GetProperties();

            if (sourceType == destinationType)
                return new MappingResult<T> { Result = entity as T, IsSuccessful = true };

            foreach (var property in sourceProperties)
            {
                PropertyInfo destinationProperty = destinationProperties.FirstOrDefault(x => x.Name == property.Name);

                if (destinationProperty != null)
                {
                    // Check if the property types are different
                    if (destinationProperty.PropertyType != property.PropertyType)
                    {
                        return new MappingResult<T>
                        {
                            IsSuccessful = false,
                            ErrorMessage =
                                $"Type mismatch for property '{property.Name}': Source type '{property.PropertyType}' and Destination type '{destinationProperty.PropertyType}'."
                        };
                    }

                    // If types are the same and property is writable, set the value
                    if (destinationProperty.CanWrite && property.CanRead)
                    {
                        destinationProperty.SetValue(destination, property.GetValue(entity));
                    }
                }
            }

            return new MappingResult<T> { Result = destination, IsSuccessful = true };
        }
        catch (Exception e)
        {
            return new MappingResult<T> { IsSuccessful = false, ErrorMessage = e.StackTrace };
        }
    }
}

public class MappingResult<TDestination>
{
    public TDestination Result { get; set; }
    public bool IsSuccessful { get; set; }
    public string ErrorMessage { get; set; }
}