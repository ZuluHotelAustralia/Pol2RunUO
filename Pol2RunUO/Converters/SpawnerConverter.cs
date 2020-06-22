using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Pol2RunUO.Algorithms;
using Pol2RunUO.Readers;
using Pol2RunUO.Mappings;
using Server;
using Server.Misc;
using Server.Mobiles;

namespace Pol2RunUO.Converters
{
    internal class SpawnerConverter
    {
        private static IEnumerable<Type> _assemblyTypes;

        public List<PolSpawner> PolSpawners { get; private set; }

        public IEnumerable<Dictionary<string, string>> PolData { get; private set; }


        public void ImportPolData(string itemFile)
        {
            using TextReader reader = new StringReader(File.ReadAllText(itemFile));
            PolData = PolDataFileReader.ReadDataFile(reader).Where(x => x.ContainsKey("CProp_PointData")).ToList();
            PolSpawners = ReadPolSpawners();
        }

        public void ExportToXmlSpawner(string xmlFile, Dictionary<string, string[]> mappings = null)
        {
            mappings ??= BuildPolToRunUoMobileMapping();

            var xmlSpawners = new ArrayList();
            MapDefinitions.Configure();
            foreach (var polSpawner in PolSpawners)
            {
                xmlSpawners.Add(PolSpawnerToXmlSpawner(polSpawner, mappings));
            }

            using var fs = new FileStream(xmlFile, FileMode.Create);

            XmlSpawner.SaveSpawnList(xmlSpawners, fs);
        }

        public XmlSpawner PolSpawnerToXmlSpawner(PolSpawner polSpawner, Dictionary<string, string[]> mappings)
        {
            XmlSpawner xmlSpawner = new XmlSpawner(Serial.Zero)
            {
                Name = $"[POLConverted] SpawnPoint {polSpawner.Serial}",
                X = polSpawner.X,
                Y = polSpawner.Y,
                Z = polSpawner.Z,
                MaxCount = polSpawner.Max,
                SpawnRange = polSpawner.AppearRange,
                Running = false, // Stop the spawner from generating events, set the private field later
                Group = polSpawner.SpawnInGroup,
                DespawnTime = TimeSpan.FromSeconds(polSpawner.ExpireTime),
                HomeRange = polSpawner.WanderRange,
                MinDelay = TimeSpan.FromMinutes(polSpawner.Frequency),
                MaxDelay = TimeSpan.FromMinutes(polSpawner.Frequency),
                ProximityRange = -1,
                ProximitySound = 500,
                HomeRangeIsRelative = true,
            };
            
            
            if (polSpawner.StartSpawningHours != polSpawner.EndSpawningHours)
            {
                xmlSpawner.TODStart = new TimeSpan(0, polSpawner.StartSpawningHours, 0, 0);
                xmlSpawner.TODEnd = new TimeSpan(0, polSpawner.EndSpawningHours, 0, 0);
            }

            xmlSpawner.m_SpawnObjects = new ArrayList(
                polSpawner.Template
                    .Where(mappings.ContainsKey)
                    .GroupBy(x => x)
                    .OrderByDescending(group => group.Count())
                    .Select(t => new XmlSpawner.SpawnObject(mappings[t.Key].FirstOrDefault() ?? $"MissingPolNpc.{t.Key}", t.Count()))
                    .ToArray()
            );

            // We need to set these private fields with reflection
            // Otherwise their public setters will trigger calls into a Server.Core that doesn't exist!
            SetPrivateFieldValue(xmlSpawner, "m_Map", Map.Felucca);
            SetPrivateFieldValue(xmlSpawner, "m_UniqueId", Guid.NewGuid().ToString());
            SetPrivateFieldValue(xmlSpawner, "m_Running", true);

            return xmlSpawner;
        }

        private static void SetPrivateFieldValue<T>(object obj, string propName, T val)
        {
            if (obj == null) throw new ArgumentNullException(nameof(obj));
            Type t = obj.GetType();
            FieldInfo fi = null;
            while (fi == null && t != null)
            {
                fi = t.GetField(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                t = t.BaseType;
            }

            if (fi == null)
                throw new ArgumentOutOfRangeException(nameof(propName),
                    $"Field {propName} was not found in Type {obj.GetType().FullName}");
            fi.SetValue(obj, val);
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

        public static IEnumerable<string> FindNearestMobileByTypeName(string template)
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
                select t.Name;
        }

        private List<PolSpawner> ReadPolSpawners()
        {
            var spawners = new List<PolSpawner>();

            foreach (var spawnerItem in PolData)
            {
                using TextReader packedReader = new StringReader(spawnerItem["CProp_PointData"]);
                var unpacked = (object[]) PolDataFileReader.UnpackElement(packedReader);

                if (!string.Equals((string) unpacked[0], "NPC", StringComparison.InvariantCultureIgnoreCase))
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
                            if (obj is string s)
                                templates.Add(s);
                        }

                        break;
                    }
                }

                try
                {
                    PolSpawner spawner = new PolSpawner();

                    spawner.Serial = spawnerItem["Serial"];
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