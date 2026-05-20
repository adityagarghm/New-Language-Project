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
            if (e.Button == SButton.T)
                TryActivateTimeCharm();

            if (e.Button == SButton.L)
                SetLinkedSourceChest();

            if (e.Button == SButton.K)
                SetLinkedTargetChest();

            if (e.Button == SButton.J)
                MoveOneStackBetweenLinkedChests();

            if (e.Button == SButton.C)
                SetConveyorSourceChest();

            if (e.Button == SButton.V)
                SetConveyorOutputChest();

            if (e.Button == SButton.B)
                RunConveyorOnce(showMessage: true);

            if (e.Button == SButton.P)
                AutoPetAnimalsAndPets();
        }

        private void TryActivateTimeCharm()
        {
            if (slowedTimeAdvancesRemaining > 0)
            {
                Game1.addHUDMessage(new HUDMessage("Time is already slowed.", HUDMessage.error_type));
                return;
            }

            if (!RemoveItems(Game1.player.Items, WoodId, 10))
            {
                Game1.addHUDMessage(new HUDMessage("Need 10 wood to slow time.", HUDMessage.error_type));
                return;
            }

            slowedTimeAdvancesRemaining = 6;
            shouldBlockNextTimeAdvance = true;
            Game1.addHUDMessage(new HUDMessage("Time bends around you for 1 in-game hour.", HUDMessage.newQuest_type));
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

            RunConveyorOnce(showMessage: false);
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

        private void SetConveyorSourceChest()
        {
            Chest? chest = GetFacingChest();

            if (chest is null)
            {
                Game1.addHUDMessage(new HUDMessage("Face a wheat chest first.", HUDMessage.error_type));
                return;
            }

            conveyorSourceChest = GetCurrentChestLink();
            Game1.addHUDMessage(new HUDMessage("Conveyor source set.", HUDMessage.newQuest_type));
        }

        private void SetConveyorOutputChest()
        {
            Chest? chest = GetFacingChest();

            if (chest is null)
            {
                Game1.addHUDMessage(new HUDMessage("Face an output chest first.", HUDMessage.error_type));
                return;
            }

            conveyorOutputChest = GetCurrentChestLink();
            Game1.addHUDMessage(new HUDMessage("Conveyor output set.", HUDMessage.newQuest_type));
        }

        private void RunConveyorOnce(bool showMessage)
        {
            Chest? source = GetChest(conveyorSourceChest);
            Chest? output = GetChest(conveyorOutputChest);

            if (source is null || output is null)
            {
                if (showMessage)
                    Game1.addHUDMessage(new HUDMessage("Set conveyor source with C and output with V.", HUDMessage.error_type));

                return;
            }
            if (!RemoveItems(source.Items, WheatId, 1))
            {
                if (showMessage)
                    Game1.addHUDMessage(new HUDMessage("No wheat in conveyor source.", HUDMessage.error_type));

                return;
            }
            Item? leftover = output.addItem(new StardewValley.Object(FlourId, 1));

            if (leftover is not null)
            {
                source.addItem(new StardewValley.Object(WheatId, 1));
                if (showMessage)
                    Game1.addHUDMessage(new HUDMessage("Conveyor output chest is full.", HUDMessage.error_type));

                return;
            }

            if (showMessage)
                Game1.addHUDMessage(new HUDMessage("Conveyor milled 1 wheat into flour.", HUDMessage.newQuest_type));
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
