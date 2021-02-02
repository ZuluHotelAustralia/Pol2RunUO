using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pol2RunUO.Algorithms;
using Pol2RunUO.Readers;
using Pol2RunUO.Mappings;
using Server;
using Server.Json;
using Server.Misc;
using Server.Mobiles;
using Server.Spells;
using static Pol2RunUO.Converters.Helpers;

namespace Pol2RunUO.Converters
{
    internal class SpawnerConverter
    {

        public List<PolSpawner> PolSpawners { get; private set; }

        public IEnumerable<Dictionary<string, string>> PolData { get; private set; }


        public void ImportPolData(string itemFile)
        {
            using TextReader reader = new StringReader(File.ReadAllText(itemFile));
            PolData = PolDataFileReader.ReadDataFile(reader).Where(x => x.ContainsKey("CProp_PointData")).ToList();
            PolSpawners = ReadPolSpawners();
        }

        public void ExportToXmlSpawner(string file, Dictionary<string, string[]> mappings = null)
        {
            mappings ??= BuildPolToRunUoMobileMapping();

            MapDefinitions.Configure();
            var spawners = PolSpawners.Select(polSpawner => PolSpawnerToJsonSpawner(polSpawner, mappings)).ToList();
            
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                // WriteIndented = true
            };
            
            var jsonString = JsonSerializer.Serialize(spawners, options);
            
            File.WriteAllText(file, jsonString);
        }

        public Spawner PolSpawnerToJsonSpawner(PolSpawner polSpawner, Dictionary<string, string[]> mappings)
        {
            var delay = TimeSpan.FromMinutes(polSpawner.Frequency);

            var spawner = new Spawner
            {
                Location = new long[] {polSpawner.X, polSpawner.Y, polSpawner.Z},
                Count = polSpawner.Max,
                HomeRange = polSpawner.AppearRange,
                WalkingRange = polSpawner.WanderRange,
                MinDelay = delay.ToString(),
                MaxDelay = (delay + delay / 4).ToString(),
            };

            spawner.Entries = polSpawner.Template
                .Where(mappings.ContainsKey)
                .GroupBy(x => x)
                .OrderByDescending(group => group.Count())
                .Select(t => new Entry
                {
                    MaxCount = t.Count(),
                    Name = mappings[t.Key].FirstOrDefault() ?? $"MissingPolNpc.{t.Key}",
                    Probability = 100
                })
                .ToList();

            return spawner;
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

            var mappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var template in distinct)
            {
                // Force the loading of the RunUO assembly so we can reflect its types
                Type _ = typeof(SpellRegistry);

                var mobileTypes = FindNearestMobileByTypeName(template);
                mappings.TryAdd(template, mobileTypes.ToArray());
            }

            return mappings;
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