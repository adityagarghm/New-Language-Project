using System;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace NewLanguageProject
{
    public class ModEntry : Mod
    {
        private const string EnergyBonusKey = "adityagarg.NewLanguageProject/TemporaryEnergyBonus";

        private bool isRainyOvergrowthDay;
        private bool isWindyDay;
        private bool isHotDay;
        private bool addingBonusItem;
        private bool shouldDropWindyItems;
        private bool shouldGiveEnergyBonus;
        private bool shouldApplyHeatPenalty;

        private int pendingEnergyBonus;
        private int temporaryEnergyBonus;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            RemoveTemporaryEnergyBonus();

            shouldDropWindyItems = false;
            shouldGiveEnergyBonus = false;
            shouldApplyHeatPenalty = false;

            Game1.player.health = 500;

            if (Game1.random.NextDouble() < 0.67)
            {
                pendingEnergyBonus = Game1.random.Next(100, 151);
                shouldGiveEnergyBonus = true;
            }

            this.Monitor.Log("DayStarted event is running.", LogLevel.Info);

            isRainyOvergrowthDay = Game1.isRaining || Game1.isLightning;
            isWindyDay = Game1.isDebrisWeather;
            isHotDay = Game1.currentSeason == "summer" && !Game1.isRaining && !Game1.isLightning;

            if (isRainyOvergrowthDay)
            {
                Game1.addHUDMessage(new HUDMessage("Rainy overgrowth: crops may give extra yield today.", HUDMessage.newQuest_type));
            }

            if (isWindyDay)
            {
                Game1.addHUDMessage(new HUDMessage("Windy day: wood and seeds may appear around the farm.", HUDMessage.newQuest_type));
                shouldDropWindyItems = true;
            }

            if (isHotDay)
            {
                shouldApplyHeatPenalty = true;
                Game1.addHUDMessage(new HUDMessage("Heat wave: stamina will drain faster today.", HUDMessage.error_type));
            }
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
                    ? "388"
                    : "770";

                StardewValley.Object item = new StardewValley.Object(itemId, 1);
                Game1.createItemDebris(item, pixelPosition, Game1.random.Next(4), farm);
            }

            Game1.addHUDMessage(new HUDMessage("The wind scattered useful debris around the farm.", HUDMessage.newQuest_type));
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            if (!Context.IsWorldReady || !isHotDay)
                return;

            if (!Game1.player.currentLocation.IsOutdoors)
                return;

            Game1.player.Stamina = Math.Max(0, Game1.player.Stamina - 1f);
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

                Game1.player.Stamina = Game1.player.Stamina * 0.85f;

                this.Monitor.Log($"Heat reduced stamina once. Current stamina: {Game1.player.Stamina}", LogLevel.Info);
            }
        }
    }
}
