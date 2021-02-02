using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using Pol2RunUO.Mappings;
using Server;
using Server.Items;
using Server.Mobiles;
using Server.Spells;
using static Pol2RunUO.Converters.Helpers;

namespace Pol2RunUO.Converters
{
    internal class NpcDescMobileConverter
    {
        private readonly Dictionary<string, string> _entry;
        private readonly List<string> _overrides = new List<string>();

        private readonly SortedDictionary<string, string> _props =
            new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, List<string>> _equip =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<SkillName, string> _skills = new Dictionary<SkillName, string>();
        private readonly Dictionary<ResistanceType, string> _resistances = new Dictionary<ResistanceType, string>();
        private readonly List<string> _spells = new List<string>();

        public static void Convert(DirectoryInfo outputDir)
        {
            var duplicates = new List<Dictionary<string, string>>();
            foreach (var entry in PolData.NpcDesc)
            {
                var className = GetClassName(entry["Name"]);
                if (className != null && entry["DataElementId"] != null &&
                    !NpcDescToClassName.TryAdd(entry["DataElementId"], GetClassName(entry["Name"])))
                    duplicates.Add(entry);
            }

            foreach (var entry in PolData.NpcDesc.Except(duplicates))
            {
                var instance = new NpcDescMobileConverter(entry);
                var className = GetClassName(entry["Name"]);
                
                var output = instance.Convert(className);

                if (output == null)
                {
                    Console.WriteLine($"Skipping {className} conversion returned a null result");
                    continue;
                }
                
                string subDirectory = "";
                if (instance._props.TryGetValue("CreatureType", out var creatureType))
                {
                    subDirectory = creatureType.Split('.')[1];
                    
                    var dirInfo = new DirectoryInfo($"{outputDir.FullName}{subDirectory}");
                    if (!dirInfo.Exists)
                        dirInfo.Create();
                }

                var fileName = $"{outputDir.FullName}{subDirectory}/{className}.cs";

                if (string.IsNullOrEmpty(className))
                {
                    Console.WriteLine($"Skipping {entry["DataElementId"]}, classname is empty");
                    continue;
                }

                var exists = MobileTypes.Where(t => t.Name.Equals(className, StringComparison.InvariantCultureIgnoreCase)).ToArray();
                
                if (exists.Any() && !File.Exists(fileName))
                {

                    if (subDirectory == "Animal" || exists.Any(t => t.IsSubclassOf(typeof(BaseMount))))
                    {
                        Console.WriteLine(
                            $"Skipping {className} ({entry["DataElementId"]}) as a conflicting mobile with the same name already exists.");
                        continue;
                    }

                    DirectoryInfo dir = outputDir;
                    string projectRoot = null;
                    while (projectRoot == null)
                    {
                        dir = dir.Parent;
                        projectRoot = GetAllFiles(dir.FullName, "*.csproj").FirstOrDefault();
                    }
                    
                    var conflictingFileName = GetAllFiles(dir.FullName, $"{className}.cs")
                        .FirstOrDefault(f => !f.Contains(outputDir.FullName) && !f.Contains("Mounts") && f.Contains("Mobiles"));
                    
                    
                    if (conflictingFileName != null)
                    {
                        var text = File.ReadAllText(conflictingFileName);

                        text = text.Replace( $"class {className}", $"class {className}Old");
                        text = text.Replace( $"{className}(", $"{className}Old(");
                    
                        File.WriteAllText(conflictingFileName, text);
                        File.Move(conflictingFileName, conflictingFileName.Replace(className, $"{className}Old"));
                    }
                    else
                    {
                        continue;
                    }
                }

                File.WriteAllText(fileName, output);

                Console.WriteLine($"Processed {className}");
            }
        }


        private NpcDescMobileConverter(Dictionary<string, string> entry)
        {
            _entry = entry;
        }

        public string Convert(string className)
        {
            ProcessNpcBaseValues();
            ProcessNpcEquipEntry();
            ProcessNpcScript();
            ProcessNpcOverrides();


            if (!_props.ContainsKey("AiType"))
            {
                Console.WriteLine($"Skipping {className} as it has no assignable AiType");
                return null;
            }

            if (className == null)
            {
                Console.WriteLine($"Skipping as it has no class name: {_entry["Name"]}");
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
            try
            {
                npc.Initialize();
                string output = npc.TransformText();
                return output;
            }
            catch (Exception e)
            {
                e = e;
            }

            return null;
        }

        private void ProcessNpcOverrides()
        {
        }

        private List<Dictionary<string, string>> GetItemDescEntries(List<string> equipEntry)
        {
            var equip = new List<Dictionary<string, string>>();

            foreach (var value in equipEntry)
            {
                var lookup = value.Replace('\t', ' ');
                lookup = lookup.Contains(' ') ? lookup.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0] : lookup;

                var entry = PolData.ItemDesc.FirstOrDefault(x => 
                    x != null && 
                    x.ContainsKey("Name") &&
                    x.ContainsKey("DataElementId") &&
                    string.Equals(lookup.StartsWith("0x") ? x["DataElementId"] : x["Name"], lookup, StringComparison.InvariantCultureIgnoreCase)
                );

                if (entry != null)
                {
                    equip.Add(entry);
                }
                // Add fake entries for implied ObjTypes below the MaxObjType limit (0x20000)
                else if (ToInt(lookup) < 0x20000)
                {
                    entry = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        {"Graphic", lookup}
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

        private void ProcessEquipFromItemDescEntries(List<Dictionary<string, string>> equipItems)
        {
            foreach (var item in equipItems)
            {
                string graphic;
                if ((item.TryGetValue("graphic", out graphic) || item.TryGetValue("Graphic", out graphic)) &&
                    FindTypeForObjType(graphic, out var itemType))
                {
                    string name;

                    var statements = new List<string>();

                    if (!item.TryGetValue("name", out name) && !item.TryGetValue("Name", out name))
                        name = itemType.Name;

                    name = char.ToLowerInvariant(name[0]) + name.Substring(1);

                    statements.Add($"AddItem(new {itemType.Name}");

                    if (itemType.IsSubclassOf(typeof(Hair)))
                        statements[0] = $"{statements[0]}(Utility.RandomHairHue())";

                    statements.Add("{");

                    void Add(string p, string s) => statements.Add($"    {p} = {s},");
                    Add("Movable", "false");

                    foreach (var (prop, str) in item)
                    {
                        var value = CleanValue(str);

                        switch (prop.ToLowerInvariant())
                        {
                            case "desc":
                                Add("Name", $"\"{value}\"");
                                break;
                            case "color":
                                if (string.IsNullOrEmpty(value))
                                    break;
                                Add("Hue", value);
                                break;
                            case "ar":
                                if (itemType.IsSubclassOf(typeof(BaseArmor)))
                                    Add("BaseArmorRating", value);
                                break;
                            case "maxhp":
                                if (itemType.IsSubclassOf(typeof(BaseArmor)) ||
                                    itemType.IsSubclassOf(typeof(BaseWeapon)))
                                {
                                    Add("MaxHitPoints", value);
                                    Add("HitPoints", value);
                                }
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
                                Add("MissSound", value);
                                break;
                            case "anim":
                                Add("Animation", $"(WeaponAnimation){value}");
                                break;
                            case "attribute":
                                if (SkillNames.Contains(value) && Enum.TryParse(value, true, out SkillName skill))
                                    Add("Skill", $"SkillName.{skill}");
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

        private void ProcessNpcEquipEntry()
        {
            if (!_entry.TryGetValue("Equip", out string equipName))
                return;

            var equipEntry =
                PolData.Equip.FirstOrDefault(x => x.ContainsKey("DataElementId") && x["DataElementId"] == equipName);

            if (equipEntry == null)
                return;

            var equipItems = GetItemDescEntries(equipEntry.Values.ToList());

            var weapon = equipItems.FirstOrDefault(x =>
                x.ContainsKey("DataElementType") &&
                x["DataElementType"].Contains("Weapon", StringComparison.InvariantCultureIgnoreCase));

            var virtualArmor = equipItems.FirstOrDefault(x =>
                x.ContainsKey("Name") && x["Name"].Contains("Armor", StringComparison.InvariantCultureIgnoreCase));

            if (virtualArmor != null)
            {
                if (virtualArmor.TryGetValue("AR", out string armor))
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


            spellId = PolSpellIdToRunUO(spellId);

            chance /= 100;

            var spellName = SpellRegistry.SpellTypes[(SpellEntry) spellId].Name;


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
                case "spellkillpcsTeleporter":
                case "killpcsTeleporter":
                    _props.TryAdd("TargetAcquireExhaustion", "true");
                    break;
                case "killpcsTeleporterfast":
                    _props.TryAdd("TargetAcquireExhaustion", "true");
                    goto case "fast";
                case "killpcssprinters":
                case "fastspiders":
                case "fast":
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

            foreach (var (key, x) in _entry)
            {
                if (x == null)
                    continue;

                string value = x.Trim();
                int commentIdx = value.IndexOf("//", StringComparison.Ordinal);
                if (commentIdx > -1)
                    value = value.Substring(0, commentIdx);

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
                        _props.TryAdd("CorpseNameOverride", $"\"corpse of {value}\"");
                        break;
                    case "objtype":
                        _props.TryAdd("Body", value);
                        break;
                    case "graphic":
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
                        _props.TryAdd(ToTitleCase(key.ToLower()), ToDigitString(value));
                        break;
                    case "mana":
                        _props.TryAdd("ManaMaxSeed", ToDigitString(value));
                        break;
                    case "stam":
                        _props.TryAdd("StamMaxSeed", ToDigitString(value));
                        break;
                    case "hits":
                        _props.TryAdd("HitsMax", ToDigitString(value));
                        break;
                    case "ar":
                        _props.TryAdd("VirtualArmor", ToDigitString(value));
                        break;
                    case "cprop_rise":
                        if (NpcDescToClassName.TryGetValue(value.Substring(1), out var className))
                            _props.TryAdd("RiseCreatureType", $"typeof({className})");
                        break;
                    case "cprop_risedelay":
                        _props.TryAdd("RiseCreatureDelay", $"TimeSpan.FromSeconds({ToDigitString(value)})");
                        break;
                    case "tameskill":
                        _props.TryAdd("MinTameSkill", ToDigitString(value));
                        _props.TryAdd("Tamable", "true");
                        break;
                    case "cprop_untamable":
                        _props.TryAdd("Tamable", "false");
                        break;
                    case "provoke":
                        _props.TryAdd("ProvokeSkillOverride", ToDigitString(value));
                        break;
                    case "cprop_unprovokable":
                        _props.TryAdd("BardImmune", "true");
                        break;
                    case "saywords":
                        _props.TryAdd("SaySpellMantra", value == "0" ? "false" : "true");
                        break;
                    case "deathsnd":
                    case "deathsound":
                        var id = ToInt(value);
                        _props.TryAdd("BaseSoundID", (id - 5).ToString());
                        break;
                    case "attackdamage":
                        var dice = new LootPackDice(value);
                        var min = dice.Count + dice.Bonus;
                        var max = dice.Count * dice.Sides + dice.Bonus;
                        _props.TryAdd("DamageMax", $"{max}");
                        _props.TryAdd("DamageMin", $"{min}");
                        break;
                    case "cprop_iswarrior":
                        _props.TryAdd("ClassSpec", "SpecName.Warrior");
                        goto case "classlevel";
                    case "cprop_ismage":
                        _props.TryAdd("ClassSpec", "SpecName.Mage");
                        goto case "classlevel";
                    case "cprop_isranger":
                        _props.TryAdd("ClassSpec", "SpecName.Ranger");
                        goto case "classlevel";
                    case "classlevel":
                        _props.TryAdd("ClassLevel", ToDigitString(value));
                        break;
                    // case "cprop_noloot":
                    // case "noloot":
                    //     _props.TryAdd("DeleteCorpseOnDeath", "true");
                    //     break;
                    case "cprop_type":
                        _props.TryAdd("CreatureType", $"CreatureType.{value.Substring(1)}");
                        break;
                    default:
                        if (ProtToResistance.TryGetValue(key, out ResistanceType resist))
                            _resistances.TryAdd(resist, GetResistanceLevel(value));
                        else if (!TryAddSkill(key, value) && !TryAddSpell(key, value))
                            _props.TryAdd($"// {key}", value);

                        break;
                }
            }

            if (TryGetHides(_entry["DataElementId"], out var hideType, out var hideQuantity))
            {
                _props.TryAdd("HideType", $"HideType.{hideType}");
                _props.TryAdd("Hides", hideQuantity.ToString());
            }

            if (_skills.Any())
                _props.TryAdd("Skills", DictToString(_skills));

            if (_resistances.Any())
                _props.TryAdd("Resistances", DictToString(_resistances));

            if (_spells.Any())
                _props.TryAdd("PreferredSpells", SpellListToString(_spells));
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

        private bool TryAddSpell(string key, string value)
        {
            if (!key.Contains("spell", StringComparison.InvariantCultureIgnoreCase))
                return false;

            value = value.Replace("MassCast", string.Empty, StringComparison.InvariantCultureIgnoreCase);
            value = value.Trim();

            var spell = PolData.Spells.FirstOrDefault(x =>
            {
                return x.ContainsKey("Script") &&
                       x["Script"].Contains(value, StringComparison.InvariantCultureIgnoreCase) ||
                       x.ContainsKey("SpellScript") &&
                       x["SpellScript"].Contains(value, StringComparison.InvariantCultureIgnoreCase);
            });
            

            Type spellType;
            if (spell != null)
            {
                int spellId = PolSpellIdToRunUO(ToInt(spell["DataElementId"]).Value);
                spellType = SpellRegistry.SpellTypes[(SpellEntry) spellId];
            }
            else
            {
                spellType = SpellRegistry.SpellInfos.Keys.FirstOrDefault(x =>
                    x != null && x.Name.Equals(value, StringComparison.InvariantCultureIgnoreCase));
            }

            if (spellType != null)
                _spells.Add(spellType.ToString());

            return false;
        }
    }
}