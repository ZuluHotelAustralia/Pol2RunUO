using System.Collections.Generic;

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
}