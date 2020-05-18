using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pol2RunUO.Mappings
{
    internal static class MappingsSerializer
    {
        public static T Load<T>(string fileName)
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(fileName));
        }

        public static void Save<T>(T mapping, string fileName)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(mapping, options);
            File.WriteAllBytes(fileName, bytes);
        }
    }
}