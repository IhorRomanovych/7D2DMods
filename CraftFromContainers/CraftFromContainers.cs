﻿using HarmonyLib;
using Newtonsoft.Json;
using Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using UnityEngine;
using static ItemActionRepair;

namespace CraftFromContainers
{
    public class CraftFromContainers : IModApi
    {
        private static CraftFromContainers context;
        private static Mod mod;
        private static Dictionary<Vector3i, TEFeatureStorage> knownStorageDict = new Dictionary<Vector3i, TEFeatureStorage>();
        private static Dictionary<Vector3i, TEFeatureStorage> currentStorageDict = new Dictionary<Vector3i, TEFeatureStorage>();
        public static ModConfig config;
        public void InitMod(Mod modInstance)
        {
            LoadConfig();

            context = this;
            mod = modInstance;
            Harmony harmony = new Harmony(GetType().ToString());
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            //var fieldNames = typeof(TileEntitySecureLootContainer).GetFields()
            //.Select(field => field.Name)
            //.ToList();
            //Dbgl(fieldNames.ToString());
        }
        public void LoadConfig()
        {
            var path = Path.Combine(AedenthornUtils.GetAssetPath(this, true), "config.json");
            if (!File.Exists(path))
            {
                config = new ModConfig();
            }
            else
            {
                config = JsonConvert.DeserializeObject<ModConfig>(File.ReadAllText(path));
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(config, Formatting.Indented));
        }
        public static void Dbgl(object str, bool prefix = true)
        {
            if (config.isDebug)
                Debug.Log((prefix ? mod.DisplayName + " " : "") + str);
        }
        [HarmonyPatch(typeof(GameManager), "StartGame")]
        static class GameManager_StartGame_Patch
        {
            static void Prefix()
            {
                knownStorageDict.Clear();

            }
        }
        [HarmonyPatch(typeof(ItemActionEntryCraft), nameof(ItemActionEntryCraft.OnActivated))]
        static class ItemActionEntryCraft_OnActivated_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling ItemActionEntryCraft.OnActivated");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks)))
                    {
                        Dbgl("Adding method to add items from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.GetAllStorageStacks2))));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }
        [HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.HasItems))]
        static class XUiM_PlayerInventory_HasItems_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling XUiM_PlayerInventory_HasItems");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (i > 0 && i < codes.Count - 1 && codes[i].opcode == OpCodes.Ldc_I4_0 && codes[i + 1].opcode == OpCodes.Ret)
                    {
                        Dbgl("Replacing return value with method");
                        codes.Insert(i, codes[i - 1].Clone());
                        codes.Insert(i, codes[i - 2].Clone());
                        codes.Insert(i, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.GetTrueRemaining))));
                        codes.Insert(i, new CodeInstruction(OpCodes.Ldloc_1));
                        codes.Insert(i, new CodeInstruction(OpCodes.Ldloc_0));
                        codes.Insert(i, new CodeInstruction(OpCodes.Ldarg_1));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.RemoveItems))]
        static class XUiM_PlayerInventory_RemoveItems_Patch
        {
            public static void Prefix(IList<ItemStack> _itemStacks, int _multiplier)
            {
                for (int i = 0; i < _itemStacks.Count; i++)
                {
                    int num = _itemStacks[i].count * _multiplier;
                    Dbgl($"N33d {num} {_itemStacks[i].itemValue.ItemClass.GetItemName()}");
                }
            }
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling XUiM_PlayerInventory_RemoveItems");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Inventory), nameof(Inventory.DecItem)))
                    {
                        var ci = codes[i + 3];
                        var ciNew = new CodeInstruction(OpCodes.Ldarg_1);
                        ci.MoveLabelsTo(ciNew);
                        Dbgl("Adding method to remove from storages");
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.RemoveRemainingForCraft))));
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldloc_1));
                        codes.Insert(i + 3, new CodeInstruction(OpCodes.Ldloc_0));
                        codes.Insert(i + 3, ciNew);
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(XUiC_RecipeCraftCount), "calcMaxCraftable")]
        static class XUiC_RecipeCraftCount_calcMaxCraftable_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling XUiC_RecipeCraftCount_calcMaxCraftable");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetAllItemStacks)))
                    {
                        Dbgl("Adding method to add items from all storages");
                        codes.Insert(i + 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.GetAllStorageStacks))));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(XUiC_IngredientEntry), nameof(XUiC_IngredientEntry.GetBindingValue))]
        static class XUiC_IngredientEntry_GetBindingValue_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling XUiC_IngredientEntry.GetBindingValue");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(XUiM_PlayerInventory), nameof(XUiM_PlayerInventory.GetItemCount), new Type[] { typeof(ItemValue) }))
                    {
                        Dbgl("Adding method to add item counts from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.AddAllStoragesCountEntry))));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(XUiC_RecipeList), nameof(XUiC_RecipeList.Update))]
        static class XUiC_RecipeList_Update_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                Dbgl("Transpiling XUiC_RecipeList.Update");
                var codes = new List<CodeInstruction>(instructions);
                for (int i = 0; i < codes.Count; i++)
                {
                    if (i > 2 && codes[i].opcode == OpCodes.Call && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(XUiC_RecipeList), "BuildRecipeInfosList"))
                    {
                        var ci = codes[i - 2];
                        var ciNew = new CodeInstruction(OpCodes.Ldloc_0);
                        ci.MoveLabelsTo(ciNew);
                        Dbgl("Adding method to add items from all storages");
                        codes.Insert(i - 2, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.AddAllStorageStacks))));
                        codes.Insert(i - 2, ciNew);
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(ItemActionRepair), "CanRemoveRequiredResource")]
        static class ItemActionRepair_CanRemoveRequiredResource_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                if (true)
                    return codes;
                Dbgl("Transpiling ItemActionRepair.CanRemoveRequiredResource");
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Bag), nameof(Bag.GetItemCount)))
                    {
                        Dbgl("Adding method to count items from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.AddAllStoragesCountItem))));
                        codes.Insert(i + 1, new CodeInstruction(codes[i - 4].opcode, codes[i - 4].operand));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(ItemActionRepair), "RemoveRequiredResource")]
        static class ItemActionRepair_RemoveRequiredResource_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                //if (!config.enableForRepairAndUpgrade)
                return codes;
                Dbgl("Transpiling ItemActionRepair.RemoveRequiredResource");
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Bag), nameof(Bag.DecItem)))
                    {
                        Dbgl("Adding method to remove items from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.RemoveRemainingForUpgrade))));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldloc_0));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_0));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(ItemActionRepair), "canRemoveRequiredItem")]
        static class ItemActionRepair_canRemoveRequiredItem_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                //if (!config.enableForRepairAndUpgrade)
                return codes;
                Dbgl("Transpiling ItemActionRepair.canRemoveRequiredItem");
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Bag), nameof(Bag.GetItemCount)))
                    {
                        Dbgl("Adding method to count items from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.AddAllStoragesCountItemStack))));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_2));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        [HarmonyPatch(typeof(ItemActionRepair), "removeRequiredItem")]
        static class ItemActionRepair_removeRequiredItem_Patch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codes = new List<CodeInstruction>(instructions);
                //if (!config.enableForRepairAndUpgrade)
                return codes;
                Dbgl("Transpiling ItemActionRepair.removeRequiredItem");
                for (int i = 0; i < codes.Count; i++)
                {
                    if (codes[i].opcode == OpCodes.Callvirt && (MethodInfo)codes[i].operand == AccessTools.Method(typeof(Bag), nameof(Bag.DecItem)))
                    {
                        Dbgl("Adding method to remove items from all storages");
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CraftFromContainers), nameof(CraftFromContainers.RemoveRemainingForRepair))));
                        codes.Insert(i + 1, new CodeInstruction(OpCodes.Ldarg_2));
                        break;
                    }
                }

                return codes.AsEnumerable();
            }
        }

        private static int AddAllStoragesCountEntry(int count, XUiC_IngredientEntry entry)
        {
            return AddAllStoragesCountItem(count, entry.Ingredient.itemValue);
        }
        private static int AddAllStoragesCountItemStack(int count, ItemStack itemStack)
        {
            return AddAllStoragesCountItem(count, itemStack.itemValue);
        }
        private static int AddAllStoragesCountItem(int count, ItemValue item)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return count;
            foreach (var kvp in currentStorageDict)
            {
                var items = kvp.Value.items;
                for (int j = 0; j < items.Length; j++)
                {
                    if (items[j].itemValue.type == item.type)
                    {
                        count += items[j].count;
                    }
                }
            }
            return count;
        }

        private static ItemStack[] GetAllStorageStacks(ItemStack[] items)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return items;

            List<ItemStack> itemList = new List<ItemStack>();
            itemList.AddRange(items);
            foreach (var kvp in currentStorageDict)
            {
                itemList.AddRange(kvp.Value.items);

            }
            return itemList.ToArray();
        }
        private static List<ItemStack> GetAllStorageStacks2(List<ItemStack> items)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return items;

            List<ItemStack> itemList = new List<ItemStack>();
            itemList.AddRange(items);
            foreach (var kvp in currentStorageDict)
            {
                itemList.AddRange(kvp.Value.items);

            }
            return itemList;
        }
        private static void AddAllStorageStacks(List<ItemStack> items)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return;

            foreach (var kvp in currentStorageDict)
            {
                items.AddRange(kvp.Value.items);
            }
        }

        private static int GetTrueRemaining(IList<ItemStack> _itemStacks, int i, int numLeft)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return numLeft;

            foreach (var kvp in currentStorageDict)
            {
                var items = kvp.Value.items;
                numLeft -= GetItemCount(items, _itemStacks[i].itemValue);
                if (numLeft <= 0)
                    return numLeft;
            }
            return numLeft;
        }

        private static void RemoveRemainingForCraft(IList<ItemStack> _itemStacks, int i, int numLeft)
        {
            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return;
            Dbgl($"Trying to remove {numLeft} {_itemStacks[i].itemValue.ItemClass.GetItemName()}");
            foreach (var kvp in currentStorageDict)
            {
                var items = kvp.Value.items;
                for (int j = 0; j < items.Length; j++)
                {
                    if (items[j].itemValue.type == _itemStacks[i].itemValue.type)
                    {
                        int toRem = Math.Min(numLeft, items[j].count);
                        Dbgl($"Removing {toRem}/{numLeft} {_itemStacks[i].itemValue.ItemClass.GetItemName()}");
                        numLeft -= toRem;
                        if (items[j].count <= toRem)
                            items[j].Clear();
                        else
                            items[j].count -= toRem;

                        kvp.Value.SetModified();
                        if (numLeft <= 0)
                            return;
                    }
                }
            }
        }

        private static int RemoveRemainingForUpgrade(int numRemoved, ItemActionRepair action, ItemValue itemValue)
        {
            return numRemoved;
            //if (!config.modEnabled)
            //    return numRemoved;
            //object upgradeInfo = AccessTools.Field(typeof(ItemActionRepair), "currentUpgradeInfo").GetValue(action);
            //int totalToRemove = ((int)AccessTools.Field(upgradeInfo.GetType(), "ItemCount").GetValue(upgradeInfo));

            //if (totalToRemove <= numRemoved)
            //    return numRemoved;

            //var numLeft = totalToRemove - numRemoved;

            //ReloadStorages();

            //if (currentStorageDict.Count == 0)
            //    return numRemoved;

            //foreach (var kvp in currentStorageDict)
            //{
            //    var items = kvp.Value.items;
            //    for (int j = 0; j < items.Length; j++)
            //    {
            //        if (items[j].itemValue.type == itemValue.type)
            //        {
            //            int toRem = Math.Min(numLeft, items[j].count);
            //            numLeft -= toRem;
            //            if (items[j].count <= toRem)
            //                items[j].Clear();
            //            else
            //                items[j].count -= toRem;

            //            kvp.Value.SetModified();
            //            if (numLeft <= 0)
            //                return totalToRemove;
            //        }
            //    }
            //}
            return 0;
        }

        private static int RemoveRemainingForRepair(int numRemoved, ItemStack _itemStack)
        {
            if (!config.modEnabled)
                return numRemoved;
            int totalToRemove = _itemStack.count;

            if (totalToRemove <= numRemoved)
                return numRemoved;

            var numLeft = totalToRemove - numRemoved;

            ReloadStorages();

            if (currentStorageDict.Count == 0)
                return numRemoved;

            foreach (var kvp in currentStorageDict)
            {
                var items = kvp.Value.items;
                for (int j = 0; j < items.Length; j++)
                {
                    if (items[j].itemValue.type == _itemStack.itemValue.type)
                    {
                        int toRem = Math.Min(numLeft, items[j].count);
                        numLeft -= toRem;
                        if (items[j].count <= toRem)
                            items[j].Clear();
                        else
                            items[j].count -= toRem;

                        kvp.Value.SetModified();
                        if (numLeft <= 0)
                            return totalToRemove;
                    }
                }
            }
            return totalToRemove - numLeft;
        }

        private static void ReloadStorages()
        {
            currentStorageDict.Clear();
            var pos = GameManager.Instance.World.GetPrimaryPlayer().position;
            for (int i = 0; i < GameManager.Instance.World.ChunkClusters.Count; i++)
            {
                //Dbgl("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
                var cc = GameManager.Instance.World.ChunkClusters[i];
                ReaderWriterLockSlim sync = (ReaderWriterLockSlim)AccessTools.Field(typeof(WorldChunkCache), "sync").GetValue(cc);
                sync.EnterReadLock();
                foreach (var c in cc.chunks.dict.Values)
                {
                    //Dbgl("^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^");
                    DictionaryList<Vector3i, TileEntity> entities = (DictionaryList<Vector3i, TileEntity>)AccessTools.Field(typeof(Chunk), "tileEntities").GetValue(c);
                    foreach (var kvp in entities.dict)
                    {
                        //Container
                        //TileEntitySecureLootContainerSigned
                        //Dbgl(kvp.Value is TileEntityLootContainer);

                        //Dbgl((kvp.Value as TileEntityLootContainer).bPlayerStorage);
                        //Dbgl(!(kvp.Value as TileEntityLootContainer).bUserAccessing);
                        var loc = kvp.Value.ToWorldPos();
                        //if (config.range <= 0 || Vector3.Distance(pos, loc) < config.range)
                        //{
                        //    //TEFeatureStorage.
                        //    //Dbgl(kvp.Value.GetType().Name);
                        //    //Dbgl(.GetType().Name);
                        //    if (kvp.Value is TileEntityComposite &&
                        //            (kvp.Value as TileEntityComposite).GetFeature<TEFeatureStorage>() is object &&
                        //            (kvp.Value as TileEntityComposite).GetFeature<TEFeatureLockable>() is object
                        //    )
                        //    {
                        //        TEFeatureStorage Storage = (kvp.Value as TileEntityComposite).GetFeature<TEFeatureStorage>();
                        //        TEFeatureLockable Lockable = (kvp.Value as TileEntityComposite).GetFeature<TEFeatureLockable>();
                        //        Dbgl(kvp.Value.GetType().Name);
                        //        Dbgl(Storage.bPlayerStorage);
                        //        Dbgl(!(kvp.Value as TileEntityComposite).IsUserAccessing());
                        //        Dbgl(!Lockable.IsLocked());
                        //        Dbgl(!Lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier));
                        //    }
                        //}

                        if (kvp.Value is TileEntityComposite &&
                             (kvp.Value as TileEntityComposite).GetFeature<TEFeatureStorage>() is object &&
                             (kvp.Value as TileEntityComposite).GetFeature<TEFeatureLockable>() is object
                        )
                        {
                            TEFeatureStorage Storage = (kvp.Value as TileEntityComposite).GetFeature<TEFeatureStorage>();
                            TEFeatureLockable Lockable = (kvp.Value as TileEntityComposite).GetFeature<TEFeatureLockable>();

                            if (Storage.bPlayerStorage && 
                                (kvp.Value as TileEntityComposite).PlayerPlaced &&
                                !(kvp.Value as TileEntityComposite).IsUserAccessing() &&
                                (!Lockable.IsLocked() || Lockable.IsUserAllowed(PlatformManager.InternalLocalUserIdentifier))
                            )
                            {
                                knownStorageDict[loc] = Storage;
                                if (config.range <= 0 || Vector3.Distance(pos, loc) < config.range)
                                {
                                    //Dbgl(kvp.Value is TileEntityLootContainer);
                                    currentStorageDict[loc] = Storage;
                                }
                            }
                        }
                    }
                }
                sync.ExitReadLock();
            }
        }
        public static int GetItemCount(ItemStack[] slots, ItemValue _itemValue)
        {
            int num = 0;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].itemValue.type == _itemValue.type)
                {
                    num += slots[i].count;
                }
            }
            return num;
        }
    }
}
