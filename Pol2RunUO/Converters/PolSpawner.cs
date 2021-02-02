using System;
using System.Collections.Generic;
using Server;

namespace Pol2RunUO.Converters
{
    internal struct PolSpawner
    {
        public string Serial;
        public string Name;
        public int X;
        public int Y;
        public int Z;
        public List<string> Template;
        public int Max;
        public int AppearRange;
        public int WanderRange;
        public int Frequency;
        public bool Disabled;
        public bool SpawnInGroup;
        public bool DespawnOnDestroy;
        public int ExpireTime;
        public int ExpireNumber;
        public int StartSpawningHours;
        public int EndSpawningHours;
        public string Notes;
    }
    
    internal partial class Spawner
    {
        public string Type { get; set; } = "Spawner";
        public long[] Location { get; set; }
        public string Map { get; set; } = "Felucca";
        public long Count { get; set; }
        public List<Entry> Entries { get; set; }
        public long HomeRange { get; set; }
        public long WalkingRange { get; set; }
        public string MaxDelay { get; set; }
        public string MinDelay { get; set; }
    }

    internal partial class Entry
    {
        public long MaxCount { get; set; }
        public string Name { get; set; }
        public long Probability { get; set; }
    }
}