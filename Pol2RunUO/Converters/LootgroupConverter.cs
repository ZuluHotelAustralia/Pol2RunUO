using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server;
using Server.Json;
using Server.Misc;
using Server.Scripts.Engines.Loot;
using Server.Text;
using static Pol2RunUO.Converters.Helpers;

// ReSharper disable IdentifierTypo
// ReSharper disable UnusedType.Global
// ReSharper disable NotAccessedField.Global
// ReSharper disable UnusedMember.Global
#pragma warning disable 1591

namespace Pol2RunUO.Converters
{
    public record PolLootgroupEntry
    {
        [JsonExtensionData] public Dictionary<string, JsonElement> data { get; set; }

        public string Count { get; set; }
        public string Name { get; set; }
        public string Chance { get; set; }
        public string Color { get; set; }
    }

    public record PolLootgroup
    {
        public List<PolLootgroupEntry> Item { get; set; } = new();
        public List<PolLootgroupEntry> Random { get; set; } = new();
        public List<PolLootgroupEntry> Stack { get; set; } = new();
    }

    public record NLootgroupsCfg
    {
        public Dictionary<string, PolLootgroup> Groups { get; set; } = new();
        public Dictionary<string, PolLootgroup> Lootgroups { get; set; } = new();
    }

    public record LootGroupEntry
    {
        public int MinQuantity { get; set; }
        public int MaxQuantity { get; set; }
        public double Chance { get; set; }
        public string Value { get; set; }
    }

    public record LootConfig
    {
        public Dictionary<string, List<LootGroupEntry>> Groups { get; set; } = new();
        public Dictionary<string, List<LootGroupEntry>> Tables { get; set; } = new();
    }

    public static class LootgroupConverter
    {
        public static void ConvertNlootgroupsCfgToJson(FileInfo nlootgroupJsonFile, DirectoryInfo dest)
        {
            // Trigger loading assemblies in the MUO Core for reflection
            var t = typeof(Server.Items.BoneArms);
            AssemblyHandler.Assemblies = AppDomain.CurrentDomain.GetAssemblies();

            var itemDesc = LoadItemDesc(Path.Join(dest.FullName, "/itemdesc.json"));

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                AllowTrailingCommas = true,
                IgnoreNullValues = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var text = File.ReadAllText(nlootgroupJsonFile.FullName, TextEncoding.UTF8);
            var result = JsonSerializer.Deserialize<NLootgroupsCfg>(text, options);


            var missing = new Dictionary<string, PolLootgroupEntry>();
            var groups = ProcessGroups(result.Groups, itemDesc, missing);
            var tables = ProcessGroups(result.Lootgroups, itemDesc, missing, groups);


            File.WriteAllText(Path.Join(dest.FullName, "/lootgroups.json"), JsonSerializer.Serialize(groups, options));
            File.WriteAllText(Path.Join(dest.FullName, "/loottables.json"), JsonSerializer.Serialize(tables, options));

            Console.WriteLine(missing.Count > 0 
                ? $"Could not match the following entries ({missing.Count}) showing nearest type names:" 
                : "No missing entries, all accounted for!"
            );
            foreach (var (name, _) in missing)
            {
                var nearestTypes = FindNearestItemByTypeName(name);
                if (nearestTypes.Any())
                {
                    var nearest = AssemblyHandler.FindTypeByName(nearestTypes.First());

                    Console.WriteLine($"\"{name.ToLowerInvariant()}\" => \"{nearest.Name}\", ");
                }
            }
        }

        private static Dictionary<string, List<LootGroupEntry>> ProcessGroups(
            Dictionary<string, PolLootgroup> entries,
            Dictionary<string, DynamicJson> itemDesc,
            Dictionary<string, PolLootgroupEntry> missing,
            Dictionary<string, List<LootGroupEntry>> groups = null
        )
        {
            var result = new Dictionary<string, List<LootGroupEntry>>();

            foreach (var (key, group) in entries)
            {
                var lg = new List<LootGroupEntry>();

                var allEntries = new List<PolLootgroupEntry>();
                allEntries.AddRange(group.Item);
                allEntries.AddRange(group.Random);
                allEntries.AddRange(group.Stack);


                for (var i = allEntries.Count - 1; i >= 0; i--)
                {
                    var e = allEntries[i];
                    var name = Rewrite(e.Name);

                    if (ShouldSkip(name, itemDesc) || string.IsNullOrWhiteSpace(name))
                        continue;

                    var (min, max) = GetMinMaxFromDiceRoll(e.Count ?? "1d1");

                    if (min == 0 && max == 0)
                        min = max = 1;

                    if (!int.TryParse(e.Chance, out int chance))
                        chance = 100;

                    var value = FindCorrespondingType(name, itemDesc)?.FullName;

                    if (value == null)
                    {
                        if (groups?.Keys.Any(k => k.InsensitiveEquals(name)) == true)
                        {
                            value = $"LootGroup.{name}"; // This is a reference to a named lootgroup
                        }
                        else
                        {
                            value = $"MissingObject.{name}"; // We can't find a type that matches, set a placeholder
                            missing.TryAdd(name, e);
                        }
                    }
                    
                    var entry = new LootGroupEntry
                    {
                        Chance = chance / 100.0,
                        MinQuantity = min,
                        MaxQuantity = max,
                        Value = value
                    };

                    lg.Add(entry);
                }

                result.Add(key, lg);
            }

            return result;
        }

        private static Dictionary<string, DynamicJson> LoadItemDesc(string path)
        {
            return JsonConfig.Deserialize<Dictionary<string, DynamicJson>>(path);
        }

        private static (string objType, Dictionary<string, JsonElement> entry) FindItemDescEntryByName(
            string name, Dictionary<string, DynamicJson> itemDesc
        )
        {
            if (name == null)
                return (null, null);
            
            var (objType, value) = itemDesc.FirstOrDefault(x =>
                x.Value.data.ContainsKey("name") &&
                x.Value.data["name"].GetString()?.ToLowerInvariant() == name.ToLowerInvariant()
            );

            return (objType, value?.data);
        }

        private static Type FindCorrespondingType(string name, Dictionary<string, DynamicJson> itemDesc)
        {
            if (name == null)
                return null;
            
            var type = AssemblyHandler.FindTypeByName(name);

            if (type == null)
            {
                var (objType, _) = FindItemDescEntryByName(name, itemDesc);

                if(objType != null)
                    FindTypeForObjType(objType, out type);
            }
            return type;
        }

        private static bool ShouldSkip(string name, Dictionary<string, DynamicJson> itemDesc)
        {

            var (_, entry) = FindItemDescEntryByName(name, itemDesc);

            if (entry != null)
            {
                if (entry.ContainsKey("cprop") && entry["cprop"].TryGetProperty("Enchanted", out var enchantElement))
                {
                    var enchant = enchantElement.GetString()?.ToLowerInvariant();

                    if (enchant == "swift" || enchant == "mystical" || enchant == "stygian")
                    {
                        return true;
                    }
                }
            }
            
            string[] skippables =
            {
                "stygian",
                "newbookspellscroll"
            };


            return skippables.Any(x => name?.Contains(x, StringComparison.InvariantCultureIgnoreCase) ?? false);
        }

        private static string Rewrite(string name)
        {
            return name?.ToLowerInvariant() switch
            {
                "radiantdiamond" => "RadiantNimbusDiamondOre",
                "ebonsapphire" => "EbonTwilightSapphireOre",
                "darkruby" => "DarkSableRubyOre",
                "spidersilk" => "SpidersSilk",
                "goldcoin" => "Gold",
                "bookoftheearth" => "earthbook",
                "codexdamnorum" => "necrobook",
                "sulphurousash" => "SulfurousAsh",
                "wormsheart" => "WyrmsHeart",
                "gmweapons" => "GMWeapon",
                "normalweapon" => "NormalWeapons",
                "gem" => "gems",
                "zuluore" => "newzuluore",
                "paladinordershield" => "OrderShield",
                "paladinchaosshield" => "ChaosShield",
                "lesserstrengthpotion" => "StrengthPotion",
                "refreshfullpotion" => "TotalRefreshPotion",
                "bonetunic" => "BoneChest",
                "tallhat" => "TallStrawHat",
                "summonwaterelemscroll" => "summonwaterelementalscroll", 
                "summonfireelemscroll" => "summonfireelementalscroll", 
                "summonearthelemscroll" => "summonearthelementalscroll", 
                "summonairelemscroll" => "summonairelementalscroll", 
                "orcmask" => "OrcishKinMask",
                "voodoomask" => "TribalMask", 
                "leatherboots" => "Boots", 
                "wizardhat" => "WizardsHat", 
                "necklace2" => "GoldNecklace", 
                "necklace1" => "GoldBeadNecklace", 
                "earrings" => "GoldEarrings", 
                "wristwatch" => "SilverBracelet", 
                "bracelet" => "GoldBracelet", 
                "ring" => "GoldRing", 
                "bananna" => "Banana", 
                "rawham" => "ham", 
                "lard" => "SlabOfBacon", 
                "muffin" => "Muffins", 
                "bread" => "BreadLoaf", 
                "pie" => "ApplePie", 
                "cheese" => "CheeseSlice", 
                "pizza" => "CheesePizza", 
                "sandles" => "Sandals", 
                "mortar" => "MortarPestle", 
                "tinker'stools" => "TinkersTools", 
                "tamborine" => "Tambourine", 
                "drum" => "Drums", 
                "jesterssuit" => "JesterSuit", 
                "platemailgorget" => "PlateGorget", 
                "platemaillegs" => "PlateLegs", 
                "platemailarms" => "PlateArms", 
                "platemailbreastplate" => "PlateChest", 
                "ringmailleggings" => "RingmailLegs", 
                "ringmailsleeves" => "RingmailGloves", 
                "ringmailtunic" => "RingmailArms", 
                "kiteshieldb1" => "MetalKiteShield", 
                "kiteshield" => "WoodenKiteShield", 
                "nosehelm" => "NorseHelm", 
                "chainmailgloves" => "RingmailGloves", 
                "chainmailtunic" => "ChainChest", 
                "chainmailleggings" => "ChainLegs", 
                "chainmailcoif" => "ChainCoif", 
                "studdedbustier" => "StuddedBustierArms", 
                "leatherbustier" => "LeatherBustierArms", 
                "femaleleather" => "FemaleLeatherChest", 
                "femalestudded" => "FemaleStuddedChest", 
                "studdedtunic" => "StuddedArms", 
                "studdedleggings" => "StuddedLegs", 
                "studdedsleeves" => "StuddedGloves", 
                "leathertunic2" => "LeatherArms", 
                "leathertunic" => "LeatherArms", 
                "leatherleggings" => "LeatherLegs", 
                "leathersleeves" => "LeatherGloves", 
                "magestaff" => "BlackStaff", 
                "smithyhammer" => "SmithHammer", 
                "clompcoconut" => "Coconut", 
                "lesseragilitypotion" => "AgilityPotion", 
                "lighthealpotion" => "HealPotion", 
                "wandofidentification" => "IDWand", 
                "magicwand33" => "ClumsyWand",
                "magicwand34" => "FeebleWand", 
                "magicwand35" => "FireballWand", 
                "magicwand36" => "GreaterHealWand", 
                "magicwand37" => "HarmWand", 
                "magicwand38" => "HealWand", 
                "magicwand39" => "LightningWand", 
                "magicwand40" => "MagicArrowWand", 
                "magicwand41" => "ManaDrainWand", 
                "magicwand42" => "WeaknessWand",
                "logs" => "Log", 
                "paganmagicitems" => "PaganMagicItem", 
                "junk" => "Junk",
                "magicweapon" => "MagicWeapons", 

                _ => name
            };
        }
    }
}