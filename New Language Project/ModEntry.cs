using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using StardewValley.Objects;

namespace NewLanguageProject
{
    public class ModEntry : Mod
    {
        private const string EnergyBonusKey = "adityagarg.NewLanguageProject/TemporaryEnergyBonus";
        private const string WoodId = "388";
        private const string StoneId = "390";
        private const string TimeCharmItemId = "adityagarg.NewLanguageProject_TimeCharm";
        private const string ConveyorItemId = "adityagarg.NewLanguageProject_Conveyor";

        private static readonly Vector2[] AdjacentDirections =
        {
            new Vector2(1, 0),
            new Vector2(-1, 0),
            new Vector2(0, 1),
            new Vector2(0, -1)
        };

        private bool isRainyOvergrowthDay;
        private bool isWindyDay;
        private bool isHotDay;
        private bool addingBonusItem;
        private bool shouldDropWindyItems;
        private bool shouldGiveEnergyBonus;
        private bool shouldApplyHeatPenalty;

        private int pendingEnergyBonus;
        private int temporaryEnergyBonus;

        private int slowedTimeAdvancesRemaining;
        private bool shouldBlockNextTimeAdvance;
        private bool isChangingTime;

        private ChestLink? linkedSourceChest;
        private ChestLink? linkedTargetChest;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Content.AssetRequested += OnAssetRequested;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            RemoveTemporaryEnergyBonus();

            shouldDropWindyItems = false;
            shouldGiveEnergyBonus = false;
            shouldApplyHeatPenalty = false;
            slowedTimeAdvancesRemaining = 0;
            shouldBlockNextTimeAdvance = false;

            Game1.player.health = Math.Max(Game1.player.health, 100);

            if (Game1.random.NextDouble() < 0.35)
            {
                pendingEnergyBonus = Game1.random.Next(50, 101);
                shouldGiveEnergyBonus = true;
            }
            isRainyOvergrowthDay = Game1.isRaining || Game1.isLightning;
            isWindyDay = Game1.isDebrisWeather;
            isHotDay = Game1.currentSeason == "summer" && !Game1.isRaining && !Game1.isLightning;

            AutoPetAnimalsAndPets();

            if (isRainyOvergrowthDay)
                Game1.addHUDMessage(new HUDMessage("Rainy overgrowth: crops may give extra yield today.", HUDMessage.newQuest_type));

            if (isWindyDay)
            {
                Game1.addHUDMessage(new HUDMessage("Windy day: wood and seeds may appear around the farm.", HUDMessage.newQuest_type));
                shouldDropWindyItems = true;
            }

            if (isHotDay)
            {
                shouldApplyHeatPenalty = true;
                Game1.addHUDMessage(new HUDMessage("Heat wave: stamina drains faster outdoors today.", HUDMessage.error_type));
            }

            this.Monitor.Log("DayStarted event is running.", LogLevel.Info);
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (e.Button == SButton.MouseRight || e.Button == SButton.ControllerA)
            {
                if (TryUseTimeCharm())
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }

                if (TryPlaceHeldItemOnConveyor(e.Cursor.GrabTile))
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
            }

            if (e.Button == SButton.L)
                SetLinkedSourceChest();

            if (e.Button == SButton.K)
                SetLinkedTargetChest();

            if (e.Button == SButton.J)
                MoveOneStackBetweenLinkedChests();

            if (e.Button == SButton.P)
                AutoPetAnimalsAndPets();
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ObjectData>().Data;

                    data[TimeCharmItemId] = new ObjectData
                    {
                        Name = "Time Charm",
                        DisplayName = "Time Charm",
                        Description = "Slows time for one in-game hour.",
                        Type = "Crafting",
                        Category = StardewValley.Object.CraftingCategory,
                        Price = 100,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 688
                    };
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, BigCraftableData>().Data;

                    data[ConveyorItemId] = new BigCraftableData
                    {
                        Name = "Conveyor",
                        DisplayName = "Conveyor",
                        Description = "Carries a placed item to the machine at the end of the belt.",
                        Price = 100,
                        Fragility = 0,
                        CanBePlacedIndoors = true,
                        CanBePlacedOutdoors = true,
                        Texture = "TileSheets/Craftables",
                        SpriteIndex = 275
                    };
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;

                    data["Time Charm"] = $"{WoodId} 10/Home/{TimeCharmItemId}/false/default/";
                    data["Conveyor"] = $"{StoneId} 20/Home/{ConveyorItemId}/true/default/";
                });
            }
        }

        private bool TryUseTimeCharm()
        {
            if (Game1.player.CurrentItem?.ItemId != TimeCharmItemId)
                return false;

            if (slowedTimeAdvancesRemaining > 0)
            {
                Game1.addHUDMessage(new HUDMessage("Time is already slowed.", HUDMessage.error_type));
                return true;
            }

            Game1.player.reduceActiveItemByOne();
            slowedTimeAdvancesRemaining = 6;
            shouldBlockNextTimeAdvance = true;
            Game1.addHUDMessage(new HUDMessage("Time bends around you for 1 in-game hour.", HUDMessage.newQuest_type));
            return true;
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            HandleTimeSlow(e);

            if (isHotDay && Game1.player.currentLocation.IsOutdoors)
                Game1.player.Stamina = Math.Max(0, Game1.player.Stamina - 1f);

            if (isRainyOvergrowthDay && Game1.player.currentLocation.IsOutdoors)
                Game1.player.health = Math.Min(500, Game1.player.health + 2);

        }

        private void HandleTimeSlow(TimeChangedEventArgs e)
        {
            if (isChangingTime || slowedTimeAdvancesRemaining <= 0)
                return;

            if (e.NewTime <= e.OldTime)
                return;

            if (shouldBlockNextTimeAdvance)
            {
                isChangingTime = true;
                Game1.timeOfDay = e.OldTime;
                isChangingTime = false;
                shouldBlockNextTimeAdvance = false;
                return;
            }

            slowedTimeAdvancesRemaining--;
            shouldBlockNextTimeAdvance = true;

            if (slowedTimeAdvancesRemaining <= 0)
                Game1.addHUDMessage(new HUDMessage("Time returns to normal.", HUDMessage.newQuest_type));
        }

        private void RemoveTemporaryEnergyBonus()
        {
            if (!Game1.player.modData.TryGetValue(EnergyBonusKey, out string? value))
                return;

            if (int.TryParse(value, out int oldBonus))
            {
                Game1.player.maxStamina.Value -= oldBonus;

                if (Game1.player.Stamina > Game1.player.MaxStamina)
                    Game1.player.Stamina = Game1.player.MaxStamina;
            }

            Game1.player.modData.Remove(EnergyBonusKey);
            temporaryEnergyBonus = 0;
        }

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (!Context.IsWorldReady || !isRainyOvergrowthDay || addingBonusItem)
                return;

            if (!Game1.player.currentLocation.IsFarm)
                return;

            foreach (var addedItem in e.Added)
            {
                if (addedItem is not StardewValley.Object item)
                    continue;

                if (item.Category != StardewValley.Object.VegetableCategory
                    && item.Category != StardewValley.Object.FruitsCategory
                    && item.Category != StardewValley.Object.flowersCategory)
                    continue;

                if (Game1.random.NextDouble() > 0.25)
                    continue;

                addingBonusItem = true;

                StardewValley.Object bonus = new StardewValley.Object(item.ItemId, 1);
                Game1.player.addItemToInventoryBool(bonus);

                addingBonusItem = false;

                Game1.addHUDMessage(new HUDMessage("+1 overgrown crop", HUDMessage.newQuest_type));
            }
        }

        private void DropWindyDayItems()
        {
            var farm = Game1.getFarm();

            for (int i = 0; i < 8; i++)
            {
                Vector2 tile = new Vector2(
                    Game1.random.Next(10, farm.map.Layers[0].LayerWidth - 10),
                    Game1.random.Next(10, farm.map.Layers[0].LayerHeight - 10)
                );

                Vector2 pixelPosition = tile * 64f;

                string itemId = Game1.random.NextDouble() < 0.65
                    ? WoodId
                    : "770";

                StardewValley.Object item = new StardewValley.Object(itemId, 1);
                Game1.createItemDebris(item, pixelPosition, Game1.random.Next(4), farm);
            }

            Game1.addHUDMessage(new HUDMessage("The wind scattered useful debris around the farm.", HUDMessage.newQuest_type));
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (shouldGiveEnergyBonus)
            {
                shouldGiveEnergyBonus = false;
                temporaryEnergyBonus = pendingEnergyBonus;

                Game1.player.maxStamina.Value += temporaryEnergyBonus;
                Game1.player.Stamina = Game1.player.MaxStamina;
                Game1.player.modData[EnergyBonusKey] = temporaryEnergyBonus.ToString();

                Game1.addHUDMessage(new HUDMessage($"You woke up energized. +{pendingEnergyBonus} max energy today.", HUDMessage.newQuest_type));
                this.Monitor.Log($"Gave player +{pendingEnergyBonus} temporary max energy. Current stamina: {Game1.player.Stamina}", LogLevel.Info);
            }

            if (shouldDropWindyItems)
            {
                shouldDropWindyItems = false;
                DropWindyDayItems();
            }

            if (shouldApplyHeatPenalty)
            {
                shouldApplyHeatPenalty = false;
                Game1.player.Stamina *= 0.85f;
                this.Monitor.Log($"Heat reduced stamina once. Current stamina: {Game1.player.Stamina}", LogLevel.Info);
            }
        }

        private void SetLinkedSourceChest()
        {
            Chest? chest = GetFacingChest();

            if (chest is null)
            {
                Game1.addHUDMessage(new HUDMessage("Face a chest first.", HUDMessage.error_type));
                return;
            }

            linkedSourceChest = GetCurrentChestLink();
            Game1.addHUDMessage(new HUDMessage("Linked source chest set.", HUDMessage.newQuest_type));
        }

        private void SetLinkedTargetChest()
        {
            Chest? chest = GetFacingChest();

            if (chest is null)
            {
                Game1.addHUDMessage(new HUDMessage("Face a chest first.", HUDMessage.error_type));
                return;
            }

            linkedTargetChest = GetCurrentChestLink();
            Game1.addHUDMessage(new HUDMessage("Linked target chest set.", HUDMessage.newQuest_type));
        }

        private void MoveOneStackBetweenLinkedChests()
        {
            Chest? source = GetChest(linkedSourceChest);
            Chest? target = GetChest(linkedTargetChest);

            if (source is null || target is null)
            {
                Game1.addHUDMessage(new HUDMessage("Set source with L and target with K.", HUDMessage.error_type));
                return;
            }

            for (int i = 0; i < source.Items.Count; i++)
            {
                Item? item = source.Items[i];

                if (item is null)
                    continue;

                source.Items[i] = null;
                Item? leftover = target.addItem(item);

                if (leftover is not null)
                    source.addItem(leftover);
                else
                    Game1.addHUDMessage(new HUDMessage("Sent item stack through linked chests.", HUDMessage.newQuest_type));

                return;
            }

            Game1.addHUDMessage(new HUDMessage("Source chest is empty.", HUDMessage.error_type));
        }

        private bool TryPlaceHeldItemOnConveyor(Vector2 conveyorTile)
        {
            GameLocation location = Game1.player.currentLocation;

            if (!location.objects.TryGetValue(conveyorTile, out StardewValley.Object conveyor) || conveyor.ItemId != ConveyorItemId)
                return false;

            Item? heldItem = Game1.player.CurrentItem;

            if (heldItem is null)
            {
                Game1.addHUDMessage(new HUDMessage("Hold an item before placing it on the conveyor.", HUDMessage.error_type));
                return true;
            }

            if (heldItem.ItemId == ConveyorItemId)
                return false;

            foreach (StardewValley.Object machine in FindMachinesAtEndOfConveyor(location, conveyorTile))
            {
                Item oneItem = heldItem.getOne();
                oneItem.Stack = 1;

                if (!machine.performObjectDropInAction(oneItem, false, Game1.player))
                    continue;

                Game1.player.reduceActiveItemByOne();
                Game1.addHUDMessage(new HUDMessage($"Conveyor sent {heldItem.DisplayName}.", HUDMessage.newQuest_type));
                return true;
            }

            Game1.addHUDMessage(new HUDMessage("The machine at the end cannot use that item.", HUDMessage.error_type));
            return true;
        }

        private IEnumerable<StardewValley.Object> FindMachinesAtEndOfConveyor(GameLocation location, Vector2 startTile)
        {
            Queue<Vector2> tilesToCheck = new Queue<Vector2>();
            HashSet<Vector2> checkedTiles = new HashSet<Vector2>();
            HashSet<Vector2> yieldedMachineTiles = new HashSet<Vector2>();

            tilesToCheck.Enqueue(startTile);
            checkedTiles.Add(startTile);

            while (tilesToCheck.Count > 0)
            {
                Vector2 tile = tilesToCheck.Dequeue();

                foreach (Vector2 direction in AdjacentDirections)
                {
                    Vector2 nextTile = tile + direction;

                    if (!location.objects.TryGetValue(nextTile, out StardewValley.Object obj))
                        continue;

                    if (obj.ItemId == ConveyorItemId)
                    {
                        if (checkedTiles.Add(nextTile))
                            tilesToCheck.Enqueue(nextTile);

                        continue;
                    }

                    if (obj is Chest)
                        continue;

                    if (yieldedMachineTiles.Add(nextTile))
                        yield return obj;
                }
            }
        }

        private void AutoPetAnimalsAndPets()
        {
            int animalsPetted = 0;
            int petsPetted = 0;

            foreach (FarmAnimal animal in Game1.getFarm().getAllFarmAnimals())
            {
                if (animal.wasPet.Value)
                    continue;

                animal.pet(Game1.player);
                animalsPetted++;
            }

            foreach (GameLocation location in Game1.locations)
            {
                foreach (NPC character in location.characters)
                {
                    if (character.GetType().Name != "Pet")
                        continue;

                    MarkPetAsPetted(character);
                    petsPetted++;
                }
            }

            if (animalsPetted > 0 || petsPetted > 0)
                Game1.addHUDMessage(new HUDMessage($"Auto-petted {animalsPetted} animals and {petsPetted} pets.", HUDMessage.newQuest_type));
        }

        private void MarkPetAsPetted(NPC pet)
        {
            SetNetValue(pet, "lastPetDay", Game1.Date.TotalDays);
            SetNetValue(pet, "friendshipTowardFarmer", 1000);
        }

        private static void SetNetValue(object target, string fieldName, int value)
        {
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field?.GetValue(target) is not object netField)
                return;

            PropertyInfo? valueProperty = netField.GetType().GetProperty("Value");
            valueProperty?.SetValue(netField, value);
        }

        private Chest? GetFacingChest()
        {
            GameLocation location = Game1.player.currentLocation;
            Vector2 tile = Game1.player.GetGrabTile();

            if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && obj is Chest chest)
                return chest;

            return null;
        }

        private ChestLink GetCurrentChestLink()
        {
            return new ChestLink(Game1.player.currentLocation.NameOrUniqueName, Game1.player.GetGrabTile());
        }

        private Chest? GetChest(ChestLink? link)
        {
            if (link is null)
                return null;

            GameLocation? location = Game1.getLocationFromName(link.LocationName);

            if (location is null)
                return null;

            if (location.objects.TryGetValue(link.Tile, out StardewValley.Object obj) && obj is Chest chest)
                return chest;

            return null;
        }

        private static bool RemoveItems(IList<Item?> items, string itemId, int count)
        {
            int remaining = count;

            for (int i = 0; i < items.Count; i++)
            {
                Item? item = items[i];

                if (item is null || item.ItemId != itemId)
                    continue;

                int amountToRemove = Math.Min(item.Stack, remaining);
                item.Stack -= amountToRemove;
                remaining -= amountToRemove;

                if (item.Stack <= 0)
                    items[i] = null;

                if (remaining <= 0)
                    return true;
            }

            return false;
        }

        private sealed class ChestLink
        {
            public ChestLink(string locationName, Vector2 tile)
            {
                LocationName = locationName;
                Tile = tile;
            }

            public string LocationName { get; }
            public Vector2 Tile { get; }
        }
    }
}
