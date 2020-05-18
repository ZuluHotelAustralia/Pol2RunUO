using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Reflection;
using Pol2RunUO.Converters;
using Pol2RunUO.Mappings;
using Pol2RunUO.Readers;

namespace Pol2RunUO
{
    class Program
    {

        static void Main(string[] args)
        {
            GetCommands().Invoke(args);
        }

        private static RootCommand GetCommands()
        {
            Command polSpawnerMappingCommand = new Command("export-spawner-mapping")
            {
                new Option("-i", "POL items.txt to read") {Argument = new Argument<FileInfo>().ExistingOnly()},
                new Option("-o", "JSON file output path") {Argument = new Argument<FileInfo>().LegalFilePathsOnly()}
            };
            polSpawnerMappingCommand.Handler = CommandHandler.Create<FileInfo, FileInfo>(ExportPolSpawnerMobileMapping);
            
            Command dumpPolDataCommand = new Command("dump-pol-data")
            {
                new Option("-i", "Datafile to read, e.g. npcdesc.cfg") {Argument = new Argument<FileInfo>().ExistingOnly()},
                new Option("-o", "JSON file output path") {Argument = new Argument<FileInfo>().LegalFilePathsOnly()}
            };
            dumpPolDataCommand.Handler = CommandHandler.Create<FileInfo, FileInfo>(DumpPolDataToJson);

            var root = new RootCommand();
            root.AddCommand(polSpawnerMappingCommand);
            root.AddCommand(dumpPolDataCommand);

            return root;
        }
        
        private static void ExportPolSpawnerMobileMapping(FileSystemInfo i, FileSystemInfo o)
        {
            var spawnerConverter = new SpawnerConverter();
            Console.WriteLine($"Importing data from {i.FullName}");

            spawnerConverter.ImportPolData(i.FullName);
            Console.WriteLine($"Read {spawnerConverter.PolData.Count()} spawnpoint entries");
            Console.WriteLine($"Parsed {spawnerConverter.PolSpawners.Count} NPC spawnpoints");

            var mappings = spawnerConverter.BuildPolToRunUoMobileMapping();
            
            Console.WriteLine($"Mapped {mappings.Count(m => m.Value.Length > 0)} npc desc entries to potential RunUO Mobiles");
            Console.WriteLine($"Couldn't find matches for {mappings.Count(m => m.Value.Length == 0)} npc desc entries.");

            MappingsSerializer.Save(spawnerConverter.BuildPolToRunUoMobileMapping(), o.FullName);
            
            Console.WriteLine($"Output written to file://{o.FullName}");
        }

        private static void DumpPolDataToJson(FileInfo i, FileInfo o)
        {
            using TextReader reader = new StringReader(File.ReadAllText(i.FullName));
            Console.WriteLine($"Importing data from {i.FullName}");

            var data = PolDataFileReader.ReadDataFile(reader);
            Console.WriteLine($"Read {data.Count} entries");
            MappingsSerializer.Save(data, o.FullName);
            Console.WriteLine($"Output written to file://{o.FullName}");

        }
        
    }
}