using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Pol2RunUO.Algorithms;
using Pol2RunUO.Mappings;
using Server;
using Server.Items;
using Server.Mobiles;
using static System.Globalization.CultureInfo;

namespace Pol2RunUO.Converters
{
    internal static class Helpers
    {
        private static IEnumerable<Type> _assemblyTypes;
        public static readonly string[] SkillNames = Enum.GetNames(typeof(SkillName));

        public static Dictionary<string, string> NpcDescToClassName =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static readonly Dictionary<string, AIType> NpcScriptToAiType =
            new Dictionary<string, AIType>(StringComparer.OrdinalIgnoreCase)
            {
                {"animal", AIType.AI_Animal},
                {"archerkillpcs", AIType.AI_Archer},
                {"barker", AIType.AI_Animal},
                {"explosionkillpcs", AIType.AI_Archer},
                {"firebreather", AIType.AI_Melee},
                {"killpcs", AIType.AI_Melee},
                {"killpcssprinters", AIType.AI_Melee},
                {"killpcsTeleporter", AIType.AI_Melee},
                {"killpcsTeleporterfast", AIType.AI_Melee},
                {"slime", AIType.AI_Melee},
                {"spellkillpcs", AIType.AI_Mage},
                {"critterhealer", AIType.AI_Mage},
                {"spellkillpcsTeleporter", AIType.AI_Mage},
                {"fastspiders", AIType.AI_Melee},
                {"spiders", AIType.AI_Melee},
                {"vortexai", AIType.AI_Melee},
                {"wolf", AIType.AI_Melee},
                {"goodcaster", AIType.AI_Mage},
                {"elfspellkillpcs", AIType.AI_Mage},
                {"daves_healer", AIType.AI_Healer},
                {"firebreatherspells", AIType.AI_Melee},
            };

        public static readonly Dictionary<string, ResistanceType> ProtToResistance =
            new Dictionary<string, ResistanceType>(StringComparer.OrdinalIgnoreCase)
            {
                {"CProp_FireProtection", ResistanceType.Fire},
                {"CProp_WaterProtection", ResistanceType.Cold},
                {"CProp_PermPoisonImmunity", ResistanceType.Poison},
                {"CProp_PhysicalProtection", ResistanceType.Physical},
                {"CProp_AirProtection", ResistanceType.Energy},
            };


        public static readonly Func<string, string> ToTitleCase = CurrentCulture.TextInfo.ToTitleCase;

        static Helpers()
        {
            _assemblyTypes ??= (from a in AppDomain.CurrentDomain.GetAssemblies()
                where a.FullName?.Contains("ZuluContent") == true
                from t in a.GetTypes()
                where t.FullName != null
                select t).ToList();
        }

        public static (int min, int max) GetMinMaxFromDiceRoll(string value)
        {
            var dice = new LootPackDice(value.ToLowerInvariant());
            var min = dice.Count + dice.Bonus;
            var max = dice.Count * dice.Sides + dice.Bonus;

            return (min, max);
        }

        public static readonly Func<string, string> ToCamelCase = value =>
        {
            value = CleanValue(value);
            value = ToTitleCase(value);
            value = char.ToLowerInvariant(value[0]) + value.Substring(1);
            return value;
        };

        public static int? ToInt(string value)
        {
            try
            {
                value = CleanValue(value);
                return value.StartsWith("0x") ? Convert.ToInt32(value, 16) : int.Parse(value);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string TrimAfter(this string value, string pattern)
        {
            int patternIdx = value.IndexOf(pattern, StringComparison.Ordinal);
            if (patternIdx > -1)
                value = value.Substring(0, patternIdx);

            return value;
        }

        public static string CleanValue(string value)
        {
            return value?.TrimAfter("//").TrimAfter("#").Trim();
        }

        public static readonly Type[] AllTypes = (
            from a in AppDomain.CurrentDomain.GetAssemblies()
            where a.FullName != null && a.FullName.Contains("RunZH")
            from t in a.GetTypes()
            select t
        ).ToArray();

        public static readonly Type[] ItemTypes = (
            from t in AllTypes
            where t.IsSubclassOf(typeof(Item))
            select t
        ).ToArray();

        public static readonly Type[] MobileTypes = (
            from t in AllTypes
            where t.IsSubclassOf(typeof(Mobile))
            select t
        ).ToArray();

        public static readonly Dictionary<Type, FlipableAttribute> TypesWithFlippable = (
            from t in AllTypes
            let attributes = t.GetCustomAttributes(typeof(FlipableAttribute), false)
            where attributes != null && attributes.Length > 0
            select new KeyValuePair<Type, FlipableAttribute>(
                t,
                attributes.Cast<FlipableAttribute>().FirstOrDefault()
            )
        ).ToDictionary(x => x.Key, x => x.Value);


        public static List<string> FindNearestTypeByName(string name, Func<string, bool> filter = null)
        {

            var needle = new string(name.ToCharArray().Where(char.IsLetter).ToArray());

            return (from t in ItemTypes
                orderby LevenshteinDistance.Calculate(needle, t.Name)
                select t.Name).ToList();
        }
        
        public static List<string> FindNearestItemByTypeName(string template)
        {
            return FindNearestTypeByName(
                template, 
                s => s.Contains(".Item.") && !s.Contains('+')
            );
        }

        public static List<string> FindNearestMobileByTypeName(string template)
        {
            return FindNearestTypeByName(
                template, 
                s => s.Contains(".Mobiles.") && !s.Contains('+') && !s.Contains("Summoned")
            );
        }

        public static bool FindTypeForObjType(string value, out Type itemType)
        {
            var objType = ToInt(value);

            if (!objType.HasValue)
            {
                itemType = null;
                Console.WriteLine($"Could not parse int for objType {value}");
                return false;
            }

            switch (objType)
            {
                case 0x108A: // AR ring
                    itemType = null;
                    return false;
            }

            if (Hair.HairById.TryGetValue(objType.Value, out itemType))
                return true;


            foreach (var (type, attribute) in TypesWithFlippable)
            {
                if (attribute.ItemIDs != null && attribute.ItemIDs.Contains(objType.Value))
                {
                    itemType = type;
                    return true;
                }
            }


            // Console.WriteLine($"Could not find itemType for objType {value}");
            return false;
        }

        public static string ToDigitString(string input)
        {
            return input == null ? string.Empty : new string(input.Where(char.IsDigit).ToArray());
        }

        public static string GetClassName(string value)
        {
            var removeWords = new[] {"The", "<random>"};

            value = value.Replace("<random>", "");
            value = value.Replace(",", "");
            value = value.Replace("-", " ");
            value = value.Replace("'", "");

            value = Regex.Replace(value, "^(an )|(a )", "");

            var split = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            value = string.Join("", split.Select(ToTitleCase).Where(x => !removeWords.Contains(x)));

            return value;
        }

        public static int PolSpellIdToRunUO(int spellId)
        {
            if (spellId <= 64)
                spellId -= 1;
            else if (spellId <= 80)
                spellId += 35;
            else if (spellId <= 96)
                spellId += 519;

            return spellId;
        }

        public static string GetPoison(string value)
        {
            var level = value switch
            {
                "1" => "Lesser",
                "2" => "Regular",
                "3" => "Regular",
                "4" => "Greater",
                "5" => "Greater",
                "6" => "Deadly",
                "7" => "Deadly",
                "8" => "Lethal",
                _ => "Regular"
            };

            return level;
        }

        public static string GetResistanceLevel(string value)
        {
            value = new string(value.Where(char.IsDigit).ToArray());

            switch (value)
            {
                case "1":
                    value = "25";
                    break;
                case "2":
                    value = "50";
                    break;
                case "3":
                    value = "75";
                    break;
                case "4":
                case "5":
                case "6":
                case "7":
                case "8":
                    value = "100";
                    break;
            }

            return value;
        }

        public static string DictToString<T>(Dictionary<T, string> dict)
        {
            if (typeof(T) == typeof(string))
            {
                return string.Join(";\n", dict.Select(x => x.Key + "=" + x.Value));
            }

            var output = new List<string>();
            output.Add($"new Dictionary<{typeof(T).Name}, CreatureProp>");
            output.Add("            {");
            foreach (var (key, value) in dict)
            {
                output.Add($"                {{ {typeof(T).Name}.{key}, {value} }},");
            }

            output.Add("            }");
            return string.Join(Environment.NewLine, output);
        }

        public static string SpellListToString(List<string> spells)
        {
            var output = new List<string>();
            output.Add($"new List<Type>");
            output.Add("            {");
            output.AddRange(spells.Select(value => $"                typeof({value.Replace("Server.", "")}),"));
            output.Add("            }");
            return string.Join(Environment.NewLine, output);
        }

        public static bool TryGetHides(string template, out string hideType, out int hideQuantity)
        {
            hideType = null;
            hideQuantity = 0;

            var entry = PolData.Corpses.FirstOrDefault(x =>
                x.ContainsKey("DataElementId") && x["DataElementId"] == template);

            if (entry == null)
                return false;

            foreach (var (key, str) in entry)
            {
                var split = CleanValue(str).Replace('\t', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (split.Length < 2 || !split[0].Contains("hide"))
                    continue;

                if (!Enum.TryParse(split[0].Replace("hide", string.Empty), true, out HideType hideEnumType))
                    continue;

                var qty = ToInt(split[1]);

                if (!qty.HasValue)
                    continue;

                hideType = hideEnumType.ToString();
                hideQuantity = qty.Value;

                return true;
            }

            return false;
        }

        public static List<string> GetAllFiles(string directory, string fileName)
        {
            return Directory.GetFiles(directory, fileName, SearchOption.AllDirectories).ToList();
        }
    }
}