using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace NewLanguageProject
{
    public class ModEntry : Mod
    {
        private bool isRainyOvergrowthDay;
        private bool isWindyDay;
        private bool isHotDay;
        private bool addingBonusItem;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            isRainyOvergrowthDay = Game1.isRaining || Game1.isLightning;
            isWindyDay = Game1.isDebrisWeather;
            isHotDay = Game1.currentSeason == "summer" && !Game1.isRaining && !Game1.isLightning;

            if (isRainyOvergrowthDay)
                Game1.addHUDMessage(new HUDMessage("Rain has caused overgrowth. Crops may yield extra.", HUDMessage.newQuest_type));

            if (isWindyDay)
                DropWindyDayItems();

            if (isHotDay)
            {
                Game1.player.Stamina *= 0.85f;
                Game1.addHUDMessage(new HUDMessage("The heat is draining your stamina.", HUDMessage.error_type));
            }
        }

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (!Context.IsWorldReady || !isRainyOvergrowthDay || addingBonusItem)
                return;

            if (!Game1.player.currentLocation.IsFarm)
                return;

            foreach (var change in e.Added)
            {
                if (change.Item is not StardewValley.Object item)
                    continue;

                if (item.Category != StardewValley.Object.CropsCategory)
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
                    ? "388" // wood
                    : "770"; // mixed seeds

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
    }
}
