using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Spells;
using static System.Globalization.CultureInfo;

namespace Pol2RunUO.Converters
{
    internal class NpcDescMobileConverter
    {
        private static readonly Func<string, string> ToTitleCase = CurrentCulture.TextInfo.ToTitleCase;

        private static readonly Func<string, string> ToCamelCase = arg =>
        {
            arg = ToTitleCase(arg);
            arg = char.ToLowerInvariant(arg[0]) + arg.Substring(1);
            return arg;
        };

        private static readonly string[] SkillNames = Enum.GetNames(typeof(SkillName));

        private static readonly Dictionary<Type, FlipableAttribute> TypesWithFlippable =
            (from a in AppDomain.CurrentDomain.GetAssemblies()
                from t in a.GetTypes()
                let attributes = t.GetCustomAttributes(typeof(FlipableAttribute), false)
                where attributes != null && attributes.Length > 0
                select new KeyValuePair<Type, FlipableAttribute>(t,
                    attributes.Cast<FlipableAttribute>().FirstOrDefault())).ToDictionary(x => x.Key, x => x.Value);

        private static Dictionary<string, string> NpcDescToClassName;
        
        private readonly Dictionary<string, string> _entry;
        private readonly List<string> _overrides = new List<string>();
        private readonly SortedDictionary<string, string> _props = new SortedDictionary<string, string>();
        private readonly Dictionary<string, List<string>> _equip = new Dictionary<string, List<string>>();

        private readonly Dictionary<SkillName, string> _skills = new Dictionary<SkillName, string>();
        private readonly Dictionary<ResistanceType, string> _resistances = new Dictionary<ResistanceType, string>();

        private static readonly Dictionary<string, AIType> NpcScriptToAiType = new Dictionary<string, AIType>
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
            {"spellkillpcsTeleporter", AIType.AI_Mage},
            {"spiders", AIType.AI_Melee},
            {"vortexai", AIType.AI_Melee},
            {"wolf", AIType.AI_Melee},
            {"goodcaster", AIType.AI_Mage},
            {"elfspellkillpcs", AIType.AI_Mage},
            {"daves_healer", AIType.AI_Healer},
            {"firebreatherspells", AIType.AI_Melee},

        };

        private static readonly Dictionary<string, ResistanceType> ProtToResistance =
            new Dictionary<string, ResistanceType>
            {
                {"CProp_FireProtection", ResistanceType.Fire},
                {"CProp_WaterProtection", ResistanceType.Cold},
                {"CProp_PermPoisonImmunity", ResistanceType.Poison},
                {"CProp_PhysicalProtection", ResistanceType.Physical},
                {"CProp_AirProtection", ResistanceType.Energy},
            };

        public static void Convert(List<Dictionary<string, string>> npcCfg, List<Dictionary<string, string>> equipCfg,
            List<Dictionary<string, string>> itemCfg, DirectoryInfo outputDir)
        {
            NpcDescToClassName = npcCfg.ToDictionary(e => e["DataElementId"], e => GetClassName(e["Name"]));
            
            foreach (var entry in npcCfg)
            {
                var instance = new NpcDescMobileConverter(entry);
                var className = GetClassName(entry["Name"]);

                var exists = SpawnerConverter.FindNearestMobileByTypeName(className).Any();
                if (exists)
                {
                    Console.WriteLine(
                        $"Skipping {className} as a conflicting mobile with the same name already exists.");
                    continue;
                }

                var output = instance.Convert(className, equipCfg, itemCfg);

                if (output == null)
                {
                    Console.WriteLine($"Skipping {className} conversion returned a null result");
                    continue;
                }


                File.WriteAllText($"{outputDir.FullName}/{className}.cs", output);

                Console.WriteLine($"Processed {className}");
            }
        }

        private NpcDescMobileConverter(Dictionary<string, string> entry)
        {
            _entry = entry;
        }

        public string Convert(string className, List<Dictionary<string, string>> equipCfg,
            List<Dictionary<string, string>> itemCfg)
        {
            ProcessNpcBaseValues();
            ProcessNpcEquipEntry(equipCfg, itemCfg);
            ProcessNpcScript();
            ProcessNpcOverrides();


            if (!_props.ContainsKey("AiType"))
            {
                Console.WriteLine($"Skipping {className} as it has no assignable AiType");
                return null;
            }

            NpcClass npc = new NpcClass
            {
                Session = new Dictionary<string, object>
                {
                    {"className", className},
                    {"fields", _props},
                    {"equip", _equip},
                    {"overrides", _overrides}
                }
            };
            npc.Initialize();
            string output = npc.TransformText();

            return output;
        }

        private static string ToDigitString(string input) => new string(input.Where(char.IsDigit).ToArray());

        private static string GetClassName(string value)
        {
            var removeWords = new[] {"The", "<random>"};

            value = value.Replace("<random>", "");
            value = value.Replace(",", "");
            value = value.Replace("-", " ");


            value = Regex.Replace(value, "^(an )|(a )", "");

            var split = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            value = string.Join("", split.Select(ToTitleCase).Where(x => !removeWords.Contains(x)));

            return value;
        }

        private void ProcessNpcOverrides()
        {
            // if(entry.ContainsKey())
        }

        private List<Dictionary<string, string>> GetItemDescEntries(List<Dictionary<string, string>> itemCfg,
            List<string> equipCfg)
        {
            var equip = new List<Dictionary<string, string>>();

            foreach (var value in equipCfg)
            {
                var lookup = value.Contains('\t') ? value.Split('\t')[0] : value;
                lookup = lookup.Contains(' ') ? value.Split(' ')[0] : lookup;

                var entry = lookup.StartsWith("0x")
                    ? itemCfg.FirstOrDefault(x =>
                        x.ContainsKey("DataElementId") && string.Equals(x["DataElementId"], lookup,
                            StringComparison.InvariantCultureIgnoreCase))
                    : itemCfg.FirstOrDefault(x =>
                        x.ContainsKey("Name") &&
                        string.Equals(x["Name"], lookup, StringComparison.InvariantCultureIgnoreCase));

                if (entry != null)
                {
                    equip.Add(entry);
                }
                // Add fake entries for implied ObjTypes below the MaxObjType limit (0x20000)
                else if (lookup.StartsWith("0x") && System.Convert.ToInt32(lookup, 16) < 0x20000) 
                {
                    entry = new Dictionary<string, string>
                    {
                        { "Graphic", lookup }
                    };

                    var color = value.Replace(lookup, string.Empty);

                    int commentIdx = color.IndexOf("//", StringComparison.Ordinal);
                    if (commentIdx > -1)
                        color = color.Substring(0, commentIdx);

                    color = color.Trim();
                    
                    Color c = Color.FromName(color);
                    if (c.IsKnownColor)
                    {
                        entry.Add("Color", $"Utility.Random{c.Name}Hue()");
                    }
                    else if (!string.IsNullOrWhiteSpace(color))
                    {
                        entry.Add("Color", color.StartsWith("0x") ? color : ToDigitString(color));
                    }


                    equip.Add(entry);
                }
            }

            return equip;
        }


        private bool FindTypeForObjType(string value, out Type itemType)
        {
            int objType = value.StartsWith("0x") ? System.Convert.ToInt32(value, 16) : int.Parse(value);
            foreach (var (type, attribute) in TypesWithFlippable)
            {
                if (attribute.ItemIDs != null && attribute.ItemIDs.Contains(objType))
                {
                    itemType = type;
                    return true;
                }
            }

            itemType = null;
            return false;
        }

        private void ProcessEquipFromItemDescEntries(List<Dictionary<string, string>> equipItems)
        {
            foreach (var item in equipItems)
            {
                string graphic;
                if ((item.TryGetValue("graphic", out graphic) || item.TryGetValue("Graphic", out graphic)) &&
                    FindTypeForObjType(graphic, out var itemType))
                {

                    // if (graphic == "0x0ec4") // Skinning Knife
                    //     continue;
                    
                    string name;

                    var statements = new List<string>();

                    if (!item.TryGetValue("name", out name) && !item.TryGetValue("Name", out name))
                        name = itemType.Name;
                    
                    name = char.ToLowerInvariant(name[0]) + name.Substring(1);

                    statements.Add($"AddItem(new {itemType.Name}");
                    statements.Add("{");

                    void Add(string p, string s) => statements.Add($"    {p} = {s},");
                    Add("Movable", "false");

                    foreach (var (prop, str) in item)
                    {
                        var value = str.Trim();
                        switch (prop.ToLowerInvariant())
                        {
                            case "desc":
                                Add("Name",$"\"{value}\"");
                                break;
                            case "color":
                                Add("Hue", value);
                                break;
                            case "ar":
                                if(itemType.IsSubclassOf(typeof(BaseArmor)))
                                    Add("BaseArmorRating", value);
                                break;
                            case "maxhp":
                                Add("MaxHitPoints", value);
                                Add("HitPoints", value);
                                break;
                            case "speed":
                                Add("Speed", value);
                                break;
                            case "cprop_normalrange":
                            case "maxrange":
                                Add("MaxRange", ToDigitString(value));
                                _props.TryAdd("FightRange", ToDigitString(value));
                                break;
                            case "hitsound":
                                Add("HitSound", value);
                                break;
                            case "misssound":
                                Add("MissSound" ,value);
                                break;
                            case "anim":
                                Add("Animation",$"(WeaponAnimation){value}");
                                break;
                            case "attribute":
                                if (SkillNames.Contains(value) && Enum.TryParse(value, true, out SkillName skill))
                                    Add("Skill",$"SkillName.{skill}");
                                break;
                            case "projectileanim":
                                Add("EffectID", value);
                                break;

                        }
                    }
                    statements.Add("});");

                    _equip.Add(name, statements);
                }
            }
        }

        private void ProcessNpcEquipEntry(List<Dictionary<string, string>> equipCfg,
            List<Dictionary<string, string>> itemCfg)
        {
            if (!_entry.TryGetValue("Equip", out string equipName))
                return;

            var equipEntry =
                equipCfg.FirstOrDefault(x => x.ContainsKey("DataElementId") && x["DataElementId"] == equipName);

            if (equipEntry == null)
                return;

            var equipItems = GetItemDescEntries(itemCfg, equipEntry.Values.ToList());

            var weapon = equipItems.FirstOrDefault(x =>
                x.ContainsKey("DataElementType") &&
                x["DataElementType"].Contains("Weapon", StringComparison.InvariantCultureIgnoreCase));

            var virtualArmor = equipItems.FirstOrDefault(x =>
                x.ContainsKey("Name") && x["Name"].Contains("Armor", StringComparison.InvariantCultureIgnoreCase));

            if (virtualArmor != null)
            {
                virtualArmor.TryGetValue("AR", out string armor);
                _props.TryAdd("VirtualArmor", ToDigitString(armor));
            }

            if (weapon == null)
                return;

            ProcessEquipFromItemDescEntries(equipItems);

            foreach (var (key, value) in weapon)
            {
                switch (key.ToLowerInvariant())
                {
                    case "damage":
                        var dice = new LootPackDice(value);
                        var min = dice.Count + dice.Bonus;
                        var max = dice.Count * dice.Sides + dice.Bonus;
                        _props.TryAdd("DamageMax", $"{max}");
                        _props.TryAdd("DamageMin", $"{min}");
                        break;
                    case "hitscript":
                        if (!value.Contains("mainhit"))
                            _props.TryAdd($"// {key}", value + " /* Weapon */");

                        if (value.Contains("spellstrikescript"))
                            ProcessSpellStrikeScript(weapon);

                        if (value.Contains("blackrock"))
                        {
                            _props.TryAdd("WeaponAbility", "new BlackrockStrike()");
                            _props.TryAdd("WeaponAbilityChance", "1.0");
                        }

                        if (value.Contains("blindingscript"))
                        {
                            _props.TryAdd("WeaponAbility", "new SpellStrike<Server.Spells.Necromancy.DarknessSpell>()");
                            _props.TryAdd("WeaponAbilityChance", "1.0");
                        }

                        if (value.Contains("trielemental"))
                        {
                            _props.TryAdd("WeaponAbility", "new TriElementalStrike()");
                            _props.TryAdd("WeaponAbilityChance", "1.0");
                        }

                        if (value.Contains("banish"))
                        {
                            _props.TryAdd("AutoDispel", "true");
                        }

                        break;
                    case "cprop_poisonlvl":
                        var level = new string(value.Where(char.IsDigit).ToArray());
                        _props.TryAdd("HitPoison", $"{nameof(Poison)}.{GetPoison(level)}");
                        break;
                    case "graphic":
                    case "hitsound":
                    case "misssound":
                    case "speed":
                        _props.TryAdd($"// {key}", value + " /* Weapon */");
                        break;
                }
            }
        }

        private void ProcessSpellStrikeScript(Dictionary<string, string> weapon)
        {
            /*
             	CProp	ChanceOfEffect	i50
	            CProp	HitWithSpell	i90
	            CProp	EffectCircle	i6
             */

            double chance = -1;
            int spellId = -1;

            foreach (var (key, value) in weapon)
            {
                switch (key.ToLowerInvariant())
                {
                    case "cprop_chanceofeffect":
                        double.TryParse(ToDigitString(value), out chance);
                        break;
                    case "cprop_hitwithspell":
                        int.TryParse(ToDigitString(value), out spellId);
                        break;
                }
            }

            if (chance < 0 || spellId < 0)
                return;

            if (SpellRegistry.Count == 0)
                Initializer.Initialize();

            if (spellId <= 64)
                spellId -= 1;
            else if (spellId <= 80)
                spellId += 35;
            else if (spellId <= 96)
                spellId += 519;

            chance /= 100;

            var spellName = SpellRegistry.Types[spellId].ToString();

            _props.TryAdd("WeaponAbility", $"new SpellStrike<{spellName}>()");
            _props.TryAdd("WeaponAbilityChance", $"{chance}");
        }

        private void ProcessNpcScript()
        {
            if (!_entry.TryGetValue("script", out string script))
                return;

            if (!NpcScriptToAiType.TryGetValue(script, out AIType aiType))
                return;

            _props.TryAdd("AiType", $"{nameof(AIType)}.{aiType} /* {script} */");

            FightMode fightMode;
            if (_entry.ContainsKey("hostile") && _entry["hostile"] == "1")
                fightMode = aiType == AIType.AI_Melee ? FightMode.Aggressor : FightMode.Closest;
            else
                fightMode = FightMode.None;

            _props.TryAdd("FightMode", $"{nameof(FightMode)}.{fightMode}");
            _props.TryAdd("PerceptionRange", "10");
            _props.TryAdd("FightRange", "1");


            switch (script)
            {
                case "firebreatherspells":
                case "firebreather":
                    _props.TryAdd("HasBreath", "true");
                    break;
                case "killpcsTeleporterfast":
                case "killpcssprinters":
                case "fastspiders":
                    _props.TryAdd("ActiveSpeed", "0.150");
                    _props.TryAdd("PassiveSpeed", "0.300");
                    break;
                default:
                    _props.TryAdd("ActiveSpeed", "0.2");
                    _props.TryAdd("PassiveSpeed", "0.4");
                    break;
            }
        }

        private void ProcessNpcBaseValues()
        {
            _props.TryAdd("AlwaysAttackable", "true");
            
            foreach (var (key, value) in _entry)
            {
                switch (key.ToLowerInvariant())
                {
                    case "alignment":
                        switch (value)
                        {
                            case "evil":
                                _props.TryAdd("AlwaysMurderer", "true");
                                _props.Remove("AlwaysAttackable");
                                break;
                            case "good":
                                _props.TryAdd("InitialInnocent", "true");
                                _props.Remove("AlwaysAttackable");
                                break;
                        }

                        break;
                    case "movemode":
                        if (value.Contains('S'))
                            _props.TryAdd("CanSwim", "true");
                        if (value.Contains('A'))
                            _props.TryAdd("CanFly", "true");
                        break;
                    case "name":
                        _props.TryAdd("Name", $"\"{value}\"");
                        _props.TryAdd("CorpseNameOverride", $"\"corpse of {value}\"".Replace("<random> the", "a"));
                        break;
                    case "objtype":
                        _props.TryAdd("Body", value);
                        break;
                    case "color":
                        _props.TryAdd("Hue", value);
                        break;
                    case "gender":
                        _props.TryAdd("Female", value == "0" ? "false" : "true");
                        break;
                    case "fame":
                    case "karma":
                    case "str":
                    case "dex":
                    case "int":
                        _props.TryAdd(ToTitleCase(key.ToLower()), value);
                        break;
                    case "mana":
                        _props.TryAdd("ManaMaxSeed", value);
                        break;
                    case "stam":
                        _props.TryAdd("StamMaxSeed", value);
                        break;
                    case "hits":
                        _props.TryAdd("HitsMax", value);
                        break;
                    case "ar":
                        _props.TryAdd("VirtualArmor", value);
                        break;
                    case "cprop_rise":
                        if(NpcDescToClassName.TryGetValue(value.Trim().Substring(1), out var className))
                            _props.TryAdd("RiseCreatureType", $"typeof({className})");
                        break;
                    case "cprop_risedelay":
                        _props.TryAdd("RiseCreatureDelay", $"TimeSpan.FromSeconds({ToDigitString(value)})");
                        break;
                    case "tameskill":
                        _props.TryAdd("MinTameSkill", value);
                        _props.TryAdd("Tamable", "true");
                        break;
                    case "deathsnd":
                    case "deathsound":
                        int id = value.StartsWith("0x") ? System.Convert.ToInt32(value, 16) : int.Parse(value);
                        _props.TryAdd("BaseSoundID", (id - 5).ToString());
                        break;
                    case "attackdamage":
                        var dice = new LootPackDice(value);
                        var min = dice.Count + dice.Bonus;
                        var max = dice.Count * dice.Sides + dice.Bonus;
                        _props.TryAdd("DamageMax", $"{max}");
                        _props.TryAdd("DamageMin", $"{min}");
                        break;
                    default:
                        if (ProtToResistance.TryGetValue(key, out ResistanceType resist))
                            _resistances.TryAdd(resist, GetResistanceLevel(value));
                        else if (!TryAddSkill(key, value))
                            _props.TryAdd($"// {key}", value);
                        break;
                }
            }

            if (_skills.Any())
                _props.TryAdd("Skills", DictToString(_skills));

            if (_resistances.Any())
                _props.TryAdd("Resistances", DictToString(_resistances));
        }

        private bool TryAddSkill(string key, string value)
        {
            key = key switch
            {
                "Blacksmithy" => "Blacksmith",
                "DetectingHidden" => "DetectHidden",
                "EvaluatingIntelligence" => "EvalInt",
                "ItemIdentification" => "ItemID",
                "TasteIdentification" => "TasteID",
                "MagicResistance" => "MagicResist",
                "MaceFighting" => "Macing",
                _ => key
            };

            if (SkillNames.Contains(key) && Enum.TryParse(key, true, out SkillName skill))
            {
                _skills.TryAdd(skill, value);
                return true;
            }

            return false;
        }

        private static string DictToString<T>(Dictionary<T, string> dict)
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

        private static string GetResistanceLevel(string value)
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

        private static string GetPoison(string value)
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
    }
}