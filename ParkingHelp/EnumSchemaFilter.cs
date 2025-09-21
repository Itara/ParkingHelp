using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.ComponentModel;
using System.Reflection;

public class EnumSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (context.Type.IsEnum)
        {
            var enumDescriptions = context.Type
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field =>
                {
                    var descAttr = field.GetCustomAttribute<DescriptionAttribute>();
                    var desc = descAttr?.Description ?? field.Name;
                    var value = ((int)field.GetValue(null)).ToString();
                    return $"{value} = {desc}";
                });

            schema.Description += "<br/><b>Enum values:</b><br/>" + string.Join("<br/>", enumDescriptions);
        }
    }
}