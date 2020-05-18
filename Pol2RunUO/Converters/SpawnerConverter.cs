using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Pol2RunUO.Algorithms;
using Pol2RunUO.Readers;
using Pol2RunUO.Mappings;
using Server;

namespace Pol2RunUO.Converters
{
    internal class SpawnerConverter
    {
        private IEnumerable<Type> _assemblyTypes;

        public List<PolSpawner> PolSpawners { get; private set; }

        public IEnumerable<Dictionary<string, string>> PolData { get; private set; }


        public void ImportPolData(string itemFile)
        {
            using TextReader reader = new StringReader(File.ReadAllText(itemFile));
            PolData = PolDataFileReader.ReadDataFile(reader).Where(x => x.ContainsKey("CProp_PointData")).ToList();
            PolSpawners = ReadPolSpawners();
        }

        public void ExportToXmlSpawner(string xmlFile)
        {
            foreach (var polSpawner in PolSpawners)
            {
                PolSpawnerToXmlSpawner(polSpawner);
            }
        }
        
        public void PolSpawnerToXmlSpawner(PolSpawner polSpawner)
        {
            
        }

        public Dictionary<string, string[]> BuildPolToRunUoMobileMapping()
        {
            var allTemplates = new List<string>();

            foreach (var spawner in PolSpawners)
            {
                allTemplates.AddRange(spawner.Template);
            }

            var distinct = allTemplates.Distinct()
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 1);

            var mappings = new Dictionary<string, string[]>();
            foreach (var template in distinct)
            {
                // Force the loading of the RunUO assembly so we can reflect its types
                Type _ = typeof(ScriptCompiler);

                var mobileTypes = FindNearestMobileByTypeName(template);
                mappings.Add(template, mobileTypes.ToArray());
            }

            return mappings;
        }

        private IEnumerable<string> FindNearestMobileByTypeName(string template)
        {
            _assemblyTypes ??= from a in AppDomain.CurrentDomain.GetAssemblies()
                from t in a.GetTypes()
                where t.FullName != null 
                      && t.FullName.Contains(".Mobiles.") 
                      && !t.FullName.Contains('+')
                      && !t.FullName.Contains("Summoned")
                select t;
            
            // Remove non-alpha chars
            template = new string(template.ToCharArray().Where(char.IsLetter).ToArray());

            return from t in _assemblyTypes
                where t.FullName!.Contains(template, StringComparison.InvariantCultureIgnoreCase)
                orderby LevenshteinDistance.Calculate(template, t.Name)
                select t.FullName;
        }

        private List<PolSpawner> ReadPolSpawners()
        {
            var spawners = new List<PolSpawner>();

            foreach (var spawnerItem in PolData)
            {
                using TextReader packedReader = new StringReader(spawnerItem["CProp_PointData"]);
                var unpacked = (object[])PolDataFileReader.UnpackElement(packedReader);

                if(!string.Equals((string)unpacked[0], "NPC", StringComparison.InvariantCultureIgnoreCase))
                    continue;
                
                var templates = new List<string>();

                switch (unpacked[1])
                {
                    case string t:
                    {
                        // Bugged pol spawner with a string list of templates
                        // Clean the string and split by ','
                        if (t[0] == '{')
                        {
                            t = Regex.Replace(t, @"[{}\s]", string.Empty);
                            var splits = t.Split(',', StringSplitOptions.RemoveEmptyEntries);
                            if (splits.Length > 0)
                                templates.AddRange(splits);
                        }
                        else
                        {
                            templates.Add(t);
                        }
                        break;
                    }

                    case object[] arr:
                    {
                        foreach (object obj in arr)
                        {
                            if(obj is string s)
                                templates.Add(s);
                        }
                        break;
                    }
                }

                try
                {
                    PolSpawner spawner = new PolSpawner();

                    spawner.Name = spawnerItem.ContainsKey("Name") ? spawnerItem["Name"] : "Spawner";
                    spawner.X = ParseIntWithDefault(spawnerItem["X"]);
                    spawner.Y = ParseIntWithDefault(spawnerItem["Y"]);
                    spawner.Z = ParseIntWithDefault(spawnerItem["Z"]);
                    spawner.Template = templates;
                    spawner.Max = ParseIntWithDefault(unpacked[2]);
                    spawner.AppearRange = ParseIntWithDefault(unpacked[3]);
                    spawner.WanderRange = ParseIntWithDefault(unpacked[4]);
                    spawner.Frequency = ParseIntWithDefault(unpacked[5]);
                    spawner.Disabled = ParseIntWithDefault(unpacked[6]) != 0;
                    spawner.SpawnInGroup = ParseIntWithDefault(unpacked[7]) != 0;
                    spawner.DespawnOnDestroy = ParseIntWithDefault(unpacked[8]) != 0;
                    spawner.ExpireTime = ParseIntWithDefault(unpacked[9]);
                    spawner.ExpireNumber = ParseIntWithDefault(unpacked[10]);
                    spawner.StartSpawningHours = ParseIntWithDefault(unpacked[11]);
                    spawner.EndSpawningHours = ParseIntWithDefault(unpacked[12]);
                    spawner.Notes = (string) unpacked[13];
                    spawners.Add(spawner);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Failed to parse spawner {0}", spawnerItem["Serial"]);
                }

            }

            return spawners;
        }

        private static int ParseIntWithDefault(object o, int @default = 0)
        {
            return o switch
            {
                string s => s.Length > 0 ? int.Parse(s) : @default,
                int i => i,
                _ => @default
            };
        }
    }
}