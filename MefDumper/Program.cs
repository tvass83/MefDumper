using MefDumper.DataModel;
using MefDumper.Helpers;
using Microsoft.Diagnostics.Runtime;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition.Hosting;
using System.IO;
using System.Linq;
using WcfDumper.DataModel;
using WcfDumper.Helpers;

namespace MefDumper
{
    class Program
    {
        static void Main(string[] args)
        {
            var retCode = ArgParser.Parse(args, new string[] { "-h", "-?", "/?" }, new string[] { "-a", "-d", "-pid" });

            ValidateArguments(retCode);
            
            string switchArg = ArgParser.Switches.FirstOrDefault();

            if (switchArg != null)
            {
                switch (switchArg)
                {
                    case "-h":
                    case "-?":
                    case "/?":
                    {
                        PrintSyntaxAndExit(retCode);
                        break;
                    }
                }
            }

            var kvp = ArgParser.SwitchesWithValues.FirstOrDefault();

            if (!kvp.Equals(default(KeyValuePair<string, string>)))
            {
                switch (kvp.Key)
                {
                    case "-a":
                    {
                        var assemblies = kvp.Value.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var assembly in assemblies)
                        {
                            ValidateFile(assembly);
                        }

                        var aggrCat = new AggregateCatalog(assemblies.Select(x => new AssemblyCatalog(x)));

                        var RESULT = new List<ReflectionComposablePart>();

                        using (var container = new CompositionContainer(aggrCat))
                        {
                            foreach (var part in container.Catalog.Parts)
                            {
                                var rfc = new ReflectionComposablePart();
                                rfc.TypeName = part.ToString();

                                foreach (var import in part.ImportDefinitions)
                                {
                                    var impDef = new ImportDefinition();
                                    impDef.ContractName = import.ContractName;

                                    var s = import.ToString().Split(new string[] { "\n\t" }, StringSplitOptions.RemoveEmptyEntries);
                                    impDef.RequiredTypeIdentity = s[1].Substring(s[1].IndexOf("\t") + 1);
                                    rfc.Imports.Add(impDef);
                                }

                                foreach (var export in part.ExportDefinitions)
                                {
                                    var expDef = new ExportDefinition();
                                    expDef.ContractName = export.ContractName;
                                    expDef.TypeIdentity = (string)export.Metadata[CONST_ExportTypeIdentity];
                                    rfc.Exports.Add(expDef);
                                }

                                RESULT.Add(rfc);
                            }
                        }

                        DgmlHelper.CreateDgml($"{Guid.NewGuid().ToString()}.dgml", RESULT);
                        break;
                    }
                    case "-d":
                    {
                        string dumpFile = kvp.Value;
                        ValidateFile(dumpFile);

                        var wrapper = ClrMdHelper.LoadDumpFile(dumpFile);
                        InitAndStartProcessing(wrapper);
                        break;
                    }
                    case "-pid":
                    {
                        if (int.TryParse(kvp.Value, out int pid))
                        {
                            var wrapper = ClrMdHelper.AttachToLiveProcess(pid);
                            InitAndStartProcessing(wrapper);
                        }
                        else
                        {
                            Console.WriteLine($"ERROR: Invalid process id.");
                            Environment.Exit(1);
                        }
                        break;
                    }
                }
            }
        }

        private static void InitAndStartProcessing(DataTargetWrapper wrapper)
        {
            wrapper.TypesToDump.Add(TYPE_CompositionContainer);
            wrapper.ClrHeapIsNotWalkableCallback = () =>
            {
                Console.WriteLine("ERROR: Cannot walk the heap!");
            };

            wrapper.ClrObjectOfTypeFoundCallback = DumpTypes;
            wrapper.Process();
        }

        private static void ValidateArguments(ErrorCode errorCode)
        {
            if (errorCode != ErrorCode.Success)
            {
                PrintSyntaxAndExit(errorCode);
            }

            if (ArgParser.OrdinaryArguments.Any())
            {
                foreach (var arg in ArgParser.OrdinaryArguments)
                {
                    Console.WriteLine($"ERROR: Unexpected argument: {arg}");
                }

                PrintSyntaxAndExit(errorCode);
            }

            var switchesWithValues = ArgParser.SwitchesWithValues;
            var switches = ArgParser.Switches;

            if (switchesWithValues.Count > 1 || switches.Count > 1)
            {
                PrintSyntaxAndExit(errorCode);
            }

            if (switchesWithValues.Any() && switches.Any())
            {
                PrintSyntaxAndExit(errorCode);
            }
        }

        private static void ValidateFile(string path)
        {
            bool ret = File.Exists(path);

            if (!ret)
            {
                Console.WriteLine($"ERROR: The following file does not exist:");
                Console.WriteLine($"\t{path}");
                Environment.Exit(1);
            }
        }

        private static void PrintSyntaxAndExit(ErrorCode errorCode)
        {
            if (errorCode != ErrorCode.Success)
            {
                Console.WriteLine($"Syntax error: {errorCode}");
            }

            Console.WriteLine("Usage:");
            Console.WriteLine("MefDumper -a assembly1[;assembly2;...;assemblyN]");
            Console.WriteLine(" OR");
            Console.WriteLine("MefDumper -d dumpfile");
            Console.WriteLine(" OR");
            Console.WriteLine("MefDumper -pid processId");

            Environment.Exit(1);
        }
                
        private static void DumpTypes(ClrHeap heap, ulong obj, string type)
        {
            Console.WriteLine($"Dumping CompositionContainer @{obj:X}");

            // Check if custom ExportProviders are present
            ulong providersFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_Providers);

            if (providersFieldValue != 0)
            {
                ulong providerArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, providersFieldValue, FIELD_List);
                List<ulong> providerObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(providerArrayObj), providerArrayObj);

                if (providerObjs.Count > 0)
                {
                    Console.WriteLine("WARNING: custom ExportProvider(s) were found:");
                }

                foreach (ulong provider in providerObjs)
                {
                    ClrType itemType = heap.GetObjectType(provider);
                    Console.WriteLine($"\t{itemType.Name}");
                }
            }

            var RESULT = new Dictionary<string, ReflectionComposablePart>();

            // Get ComposablePart[] from ComposablePartExportProvider
            List<ulong> composableParts = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, obj, HIERARCHY_CompositionContainer_To_ComposableParts, 0, TYPE_ComposablePart);

            foreach (var composablePart in composableParts)
            {
                string composablePartTypeName = heap.GetObjectType(composablePart).Name;

                if (composablePartTypeName == TYPE_ReflectionComposablePart)
                {
                    var rcpDef = ClrMdHelper.GetObjectAs<ulong>(heap, composablePart, FIELD_Definition);
                    var rcp = ProcessReflectionComposablePartDefinition(heap, rcpDef);
                    rcp.IsCreated = true;
                    MergeResults(RESULT, rcp);
                }
                else if (composablePartTypeName == TYPE_SingleExportComposablePart)
                {
                    ulong export = ClrMdHelper.GetObjectAs<ulong>(heap, composablePart, FIELD_Export);
                    ulong exportedValue = ClrMdHelper.GetObjectAs<ulong>(heap, export, FIELD_ExportedValue);
                    string exportedValueTypeName = exportedValue != 0 ? heap.GetObjectType(exportedValue).Name : null;
                    bool isCached = exportedValue != 0 && exportedValueTypeName != typeof(object).FullName;

                    if (!isCached)
                    {
                        ulong realExportedValue = ClrMdHelper.GetLastObjectInHierarchy(heap, export, HIERARCHY_Func_To_ExportedValue, 0);
                        
                        if (realExportedValue != 0)
                        {
                            exportedValueTypeName = heap.GetObjectType(realExportedValue).Name;
                        }
                    }
                    
                    var exportDefinition = ClrMdHelper.GetObjectAs<ulong>(heap, export, FIELD_Definition);
                    string contract = ClrMdHelper.GetObjectAs<string>(heap, exportDefinition, FIELD_ContractName);
                    var metadata = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, exportDefinition, HIERARCHY_ExportDefinition_To_Metadata, 0, TYPE_KVP_String_Object);
                    string typeId = "";

                    foreach (var entry in metadata)
                    {
                        if (ClrMdHelper.GetStringContents(heap, entry.key) == CONST_ExportTypeIdentity)
                        {
                            typeId = ClrMdHelper.GetStringContents(heap, entry.value);
                            break;
                        }
                    }

                    var rcp = new ReflectionComposablePart();
                    rcp.TypeName = exportedValueTypeName ?? typeId;
                    rcp.IsCreated = true;
                    rcp.Exports.Add(new ExportDefinition() { ContractName = contract, TypeIdentity = typeId });

                    MergeResults(RESULT, rcp);
                }
                else
                {
                    Console.WriteLine($"WARNING: Unsupported ComposablePart was found: {composablePartTypeName}");
                }
            }

            // Check if there exists a CatalogExportProvider
            ulong catalogExProvider = ClrMdHelper.GetObjectAs<ulong>(heap, obj, FIELD_CatalogExportProvider);

            if (catalogExProvider != 0)
            {
                ulong catalogFieldValue = ClrMdHelper.GetObjectAs<ulong>(heap, catalogExProvider, FIELD_Catalog);
                HashSet<ulong> parts = new HashSet<ulong>();

                InvokeCatalogHandler(heap, catalogFieldValue, parts);

                foreach (var part in parts)
                {
                    var rcp = ProcessReflectionComposablePartDefinition(heap, part);
                    MergeResults(RESULT, rcp);
                }

                Console.WriteLine($"{RESULT.Count} parts were found: ");
                foreach (var part in RESULT.Keys)
                {
                    Console.WriteLine($"\t{part}");
                }

                Console.WriteLine();

                // Get activated auto-created parts
                var activePartObjs = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, catalogExProvider, HIERARCHY_CatalogExportProvider_To_Activated, 0, TYPE_ComposablePartDefinitionArray2);
                var activatedPartNames = new HashSet<string>();

                foreach (var activePart in activePartObjs)
                {
                    ulong partCreationInfo = ClrMdHelper.GetObjectAs<ulong>(heap, activePart.key, FIELD_CreationInfo);
                    string partType = InvokePartCreationInfoHandler(heap, partCreationInfo);
                    activatedPartNames.Add(partType);
                }

                foreach (var rcp in RESULT)
                {
                    if (activatedPartNames.Contains(rcp.Key))
                    {
                        rcp.Value.IsCreated = true;
                    }
                }

                DgmlHelper.CreateDgml($"{obj:X}.dgml", RESULT.Select(x=>x.Value));
            }
        }

        private static void MergeResults(Dictionary<string, ReflectionComposablePart> container, ReflectionComposablePart rcp)
        {
            if (container.ContainsKey(rcp.TypeName))
            {
                var rcpInContainer = container[rcp.TypeName];
                if (!rcpInContainer.IsCreated)
                {
                    rcpInContainer.IsCreated = rcp.IsCreated;
                }

                var importsToAdd = new List<ImportDefinition>();
                foreach (var import in rcp.Imports)
                {
                    if (!rcpInContainer.Imports.Contains(import, ImportDefinitionComparer.Instance))
                    {
                        importsToAdd.Add(import);
                    }
                }

                if (importsToAdd.Any())
                {
                    rcpInContainer.Imports.AddRange(importsToAdd);
                }

                var exportsToAdd = new List<ExportDefinition>();
                foreach (var export in rcp.Exports)
                {
                    if (!rcpInContainer.Exports.Contains(export, ExportDefinitionComparer.Instance))
                    {
                        exportsToAdd.Add(export);
                    }
                }

                if (exportsToAdd.Any())
                {
                    rcpInContainer.Exports.AddRange(exportsToAdd);
                }

            }
            else
            {
                container[rcp.TypeName] = rcp;
            }
        }

        private static ReflectionComposablePart ProcessReflectionComposablePartDefinition(ClrHeap heap, ulong part)
        {
            var rcp = new ReflectionComposablePart();

            ulong creationInfoObj = ClrMdHelper.GetLastObjectInHierarchy(heap, part, HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo, 0);
            ClrType creationInfoObjType = heap.GetObjectType(creationInfoObj);
            rcp.TypeName = creationInfoObjType.GetRuntimeType(creationInfoObj)?.Name ?? creationInfoObjType.Name;

            // Get ImportDefinition[]
            ulong importArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, part, FIELD_Imports);

            if (importArrayObj != 0)
            {
                List<ulong> importObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(importArrayObj), importArrayObj);

                foreach (var importDef in importObjs)
                {
                    string contract = ClrMdHelper.GetObjectAs<string>(heap, importDef, FIELD_ContractName);
                    string typeId = ClrMdHelper.GetObjectAs<string>(heap, importDef, FIELD_RequiredTypeIdentity);
                    rcp.Imports.Add(new ImportDefinition() { ContractName = contract, RequiredTypeIdentity = typeId });
                }
            }

            // Get ExportDefinition[]
            ulong exportArrayObj = ClrMdHelper.GetObjectAs<ulong>(heap, part, FIELD_Exports);

            if (exportArrayObj != 0)
            {
                List<ulong> exportObjs = ClrMdHelper.GetArrayItems(heap.GetObjectType(exportArrayObj), exportArrayObj);

                foreach (var exportDef in exportObjs)
                {
                    ulong attrExpDef = ClrMdHelper.GetObjectAs<ulong>(heap, exportDef, FIELD_ExportDefinition);
                    string contract = ClrMdHelper.GetObjectAs<string>(heap, attrExpDef, FIELD_ContractName);
                    ulong typeObj = ClrMdHelper.GetObjectAs<ulong>(heap, attrExpDef, FIELD_TypeIdentityType);

                    string typename = null;

                    if (typeObj == 0)
                    {
                        Console.WriteLine($"WARNING: Special export was found with no type identity (contract: {contract})");
                        typename = rcp.TypeName;
                    }
                    else
                    {
                        ClrType typeObjType = heap.GetObjectType(typeObj);
                        typename = typeObjType.GetRuntimeType(typeObj)?.Name ?? typeObjType.Name;
                    }

                    rcp.Exports.Add(new ExportDefinition() { ContractName = contract, TypeIdentity = typename });
                }
            }

            return rcp;
        }

        private static void ProcessAggregateCatalog(ClrHeap heap, ulong aggrCat, HashSet<ulong> parts)
        {
            List<ulong> catalogs = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, aggrCat, HIERARCHY_AggregateCatalog_To_ComposablePartCatalogs, 0, TYPE_ComposablePartCatalogArray);

            foreach (var catalog in catalogs)
            {
                // It can be any derivation of ComposablePartCatalog
                InvokeCatalogHandler(heap, catalog, parts);
            }
        }

        private static void ProcessDirectoryCatalog(ClrHeap heap, ulong dirCat, HashSet<ulong> parts)
        {
            var asmCatalogs = ClrMdHelper.GetLastObjectInHierarchyAsKVPs(heap, dirCat, HIERARCHY_DirectoryCatalog_To_AssemblyCatalogs, 0, TYPE_KVP_String_AssemblyCatalog);

            foreach (var catalog in asmCatalogs)
            {
                Console.WriteLine(catalog.key);
                // It's always an AssemblyCatalog
                InvokeCatalogHandler(heap, catalog.value, parts);
            }
        }

        private static void ProcessAssemblyCatalog(ClrHeap heap, ulong asmCat, HashSet<ulong> parts)
        {
            // It's always a TypeCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, asmCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessApplicationCatalog(ClrHeap heap, ulong appCat, HashSet<ulong> parts)
        {
            // It's always an AggregateCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, appCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessFilteredCatalog(ClrHeap heap, ulong filteredCat, HashSet<ulong> parts)
        {
            // It can be any derivation of ComposablePartCatalog
            ulong innerCatalog = ClrMdHelper.GetObjectAs<ulong>(heap, filteredCat, FIELD_InnerCatalog);
            InvokeCatalogHandler(heap, innerCatalog, parts);
        }

        private static void ProcessTypeCatalog(ClrHeap heap, ulong typeCat, HashSet<ulong> parts)
        {
            // Get all auto-created partdescriptions (ReflectionComposablePartDefinition[])
            List<ulong> partObjs = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, typeCat, HIERARCHY_TypeCatalog_To_ComposablePartDefinitions, 0, TYPE_ComposablePartDefinitionArray);

            foreach (var partObj in partObjs)
            {
                parts.Add(partObj);
            }
        }

        private static void ProcessPrismDefaultsCatalog(ClrHeap heap, ulong prismCat, HashSet<ulong> parts)
        {
            // Get all auto-created partdescriptions (ComposablePartDefinition[])
            List<ulong> partObjs = ClrMdHelper.GetLastObjectInHierarchyAsArray(heap, prismCat, HIERARCHY_PrismDefaultsCatalog_To_ComposablePartDefinitions, 0, TYPE_ComposablePartDefinitionArray);

            foreach (var partObj in partObjs)
            {
                parts.Add(partObj);
            }
        }

        private static void InvokeCatalogHandler(ClrHeap heap, ulong composablePartCatalog, HashSet<ulong> parts)
        {
            string catalogType = heap.GetObjectType(composablePartCatalog).Name;

            if (CatalogActionMappings.ContainsKey(catalogType))
            {
                CatalogActionMappings[catalogType](heap, composablePartCatalog, parts);
            }
            else
            {
                Console.WriteLine($"WARNING: unknown catalog type: '{catalogType}'");
            }
        }

        private static string ProcessAttributedPartCreationInfo(ClrHeap heap, ulong partCreationInfo)
        {
            ulong creationInfoObj = ClrMdHelper.GetObjectAs<ulong>(heap, partCreationInfo, FIELD_Type);
            ClrType creationInfoObjType = heap.GetObjectType(creationInfoObj);
            return creationInfoObjType.GetRuntimeType(creationInfoObj)?.Name ?? creationInfoObjType.Name;
        }

        private static string ProcessGenericSpecializationPartCreationInfo(ClrHeap heap, ulong partCreationInfo)
        {
            ulong creationInfoObj = ClrMdHelper.GetObjectAs<ulong>(heap, partCreationInfo, FIELD_OriginalPartCreationInfo);
            return InvokePartCreationInfoHandler(heap, creationInfoObj);
        }

        private static string InvokePartCreationInfoHandler(ClrHeap heap, ulong partCreationInfo)
        {
            string partCreationInfoType = heap.GetObjectType(partCreationInfo).Name;

            if (PartCreationInfoActionMappings.ContainsKey(partCreationInfoType))
            {
                return PartCreationInfoActionMappings[partCreationInfoType](heap, partCreationInfo);
            }
            else
            {
                Console.WriteLine($"WARNING: unknown PartCreationInfo type: '{partCreationInfoType}'");
                return null;
            }
        }

        private static readonly string[] HIERARCHY_AggregateCatalog_To_ComposablePartCatalogs = new string[] { "_catalogs", "_catalogs", "_items" };
        private static readonly string[] HIERARCHY_CatalogExportProvider_To_Activated = new string[] { "_activatedParts", "entries" };
        private static readonly string[] HIERARCHY_CompositionContainer_To_ComposableParts = new string[] { "_partExportProvider", "_parts", "_items" };
        private static readonly string[] HIERARCHY_DirectoryCatalog_To_AssemblyCatalogs = new string[] { "_assemblyCatalogs", "entries" };
        private static readonly string[] HIERARCHY_ExportDefinition_To_Metadata = new string[] { "_metadata", "m_dictionary", "entries" };
        private static readonly string[] HIERARCHY_PrismDefaultsCatalog_To_ComposablePartDefinitions = new string[] { "parts", "_items" };
        private static readonly string[] HIERARCHY_ReflectionComposablePartDefinition_To_AttributedPartCreationInfo = new string[] { "_creationInfo", "_type" };
        private static readonly string[] HIERARCHY_TypeCatalog_To_ComposablePartDefinitions = new string[] { "_parts", "_items" };
        private static readonly string[] HIERARCHY_Func_To_ExportedValue = new string[] { "_exportedValueGetter", "_target", "exportedValue" };

        private const string CONST_ExportTypeIdentity = "ExportTypeIdentity";
        private const string FIELD_Catalog = "_catalog";
        private const string FIELD_CatalogExportProvider = "_catalogExportProvider";
        private const string FIELD_ContractName = "_contractName";
        private const string FIELD_CreationInfo = "_creationInfo";
        private const string FIELD_Definition = "_definition";
        private const string FIELD_Export = "_export";
        private const string FIELD_ExportDefinition = "_exportDefinition";
        private const string FIELD_ExportedValue = "_exportedValue";
        private const string FIELD_Exports = "_exports";
        private const string FIELD_Imports = "_imports";
        private const string FIELD_InnerCatalog = "_innerCatalog";
        private const string FIELD_List = "list";
        private const string FIELD_OriginalPartCreationInfo = "_originalPartCreationInfo";
        private const string FIELD_Providers = "_providers";
        private const string FIELD_RequiredTypeIdentity = "_requiredTypeIdentity";
        private const string FIELD_Type = "_type";
        private const string FIELD_TypeIdentityType = "_typeIdentityType";

        private const string TYPE_AttributedPartCreationInfo = "System.ComponentModel.Composition.AttributedModel.AttributedPartCreationInfo";
        private const string TYPE_ComposablePart = "System.ComponentModel.Composition.Primitives.ComposablePart[]";
        private const string TYPE_ComposablePartCatalogArray = "System.ComponentModel.Composition.Primitives.ComposablePartCatalog[]";
        private const string TYPE_ComposablePartDefinitionArray = "System.ComponentModel.Composition.Primitives.ComposablePartDefinition[]";
        private const string TYPE_ComposablePartDefinitionArray2 = "System.Collections.Generic.Dictionary+Entry<System.ComponentModel.Composition.Primitives.ComposablePartDefinition,System.ComponentModel.Composition.Hosting.CatalogExportProvider+CatalogPart>[]";
        private const string TYPE_CompositionContainer = "System.ComponentModel.Composition.Hosting.CompositionContainer";
        private const string TYPE_GenericSpecializationPartCreationInfo = "System.ComponentModel.Composition.ReflectionModel.GenericSpecializationPartCreationInfo";
        private const string TYPE_KVP_String_AssemblyCatalog = "System.Collections.Generic.Dictionary+Entry<System.String,System.ComponentModel.Composition.Hosting.AssemblyCatalog>[]";
        private const string TYPE_KVP_String_Object = "System.Collections.Generic.Dictionary+Entry<System.String,System.Object>[]";
        private const string TYPE_ReflectionComposablePart = "System.ComponentModel.Composition.ReflectionModel.ReflectionComposablePart";
        private const string TYPE_SingleExportComposablePart = "System.ComponentModel.Composition.Hosting.CompositionBatch+SingleExportComposablePart";

        private const string TYPE_AggregateCatalog = "System.ComponentModel.Composition.Hosting.AggregateCatalog";
        private const string TYPE_ApplicationCatalog = "System.ComponentModel.Composition.Hosting.ApplicationCatalog";
        private const string TYPE_AssemblyCatalog = "System.ComponentModel.Composition.Hosting.AssemblyCatalog";
        private const string TYPE_DirectoryCatalog = "System.ComponentModel.Composition.Hosting.DirectoryCatalog";
        private const string TYPE_FilteredCatalog = "System.ComponentModel.Composition.Hosting.FilteredCatalog";
        private const string TYPE_PrismDefaultsCatalog = "Prism.Mef.PrismDefaultsCatalog";
        private const string TYPE_TypeCatalog = "System.ComponentModel.Composition.Hosting.TypeCatalog";

        private static readonly Dictionary<string, Action<ClrHeap, ulong, HashSet<ulong>>> CatalogActionMappings = new Dictionary<string, Action<ClrHeap, ulong, HashSet<ulong>>>
        {
            { TYPE_FilteredCatalog, ProcessFilteredCatalog },
            { TYPE_ApplicationCatalog, ProcessApplicationCatalog },
            { TYPE_AggregateCatalog, ProcessAggregateCatalog },
            { TYPE_DirectoryCatalog, ProcessDirectoryCatalog },
            { TYPE_AssemblyCatalog, ProcessAssemblyCatalog },
            { TYPE_TypeCatalog, ProcessTypeCatalog },
            { TYPE_PrismDefaultsCatalog, ProcessPrismDefaultsCatalog }
        };

        private static readonly Dictionary<string, Func<ClrHeap, ulong, string>> PartCreationInfoActionMappings = new Dictionary<string, Func<ClrHeap, ulong, string>>
        {
            { TYPE_AttributedPartCreationInfo, ProcessAttributedPartCreationInfo },
            { TYPE_GenericSpecializationPartCreationInfo, ProcessGenericSpecializationPartCreationInfo }
        };
    }
}
