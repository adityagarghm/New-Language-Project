using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace NewLanguageProject
{
    public class ModEntry : Mod
    {
        private const string EnergyBonusKey = "adityagarg.NewLanguageProject/TemporaryEnergyBonus";
        private const string WoodId = "388";
        private const string StoneId = "390";
        private const string TimeCharmItemId = "adityagarg.NewLanguageProject_TimeCharm";
        private const string ConveyorItemId = "adityagarg.NewLanguageProject_Conveyor";
        private const string TeleporterItemId = "adityagarg.NewLanguageProject_Teleporter";
        private const string ButcherKnifeItemId = "adityagarg.NewLanguageProject_ButcherKnife";
        private const string RawMeatItemId = "adityagarg.NewLanguageProject_RawMeat";
        private const string CookedMeatItemId = "adityagarg.NewLanguageProject_CookedMeat";

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
        private bool isChoosingTeleportDestination;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.GameLoop.DayEnding += OnDayEnding; // Fix for compounding energy
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.Player.Warped += OnWarped;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            RemoveTemporaryEnergyBonus(); // Failsafe cleanup

            shouldDropWindyItems = false;
            shouldGiveEnergyBonus = false;
            shouldApplyHeatPenalty = false;
            slowedTimeAdvancesRemaining = 0;
            shouldBlockNextTimeAdvance = false;

            Game1.player.health = Math.Max(Game1.player.health, Game1.player.maxHealth);

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
                Game1.addHUDMessage(new HUDMessage("Rainy overgrowth: crops yield more and you heal outdoors today.", HUDMessage.newQuest_type));

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

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            // Remove the energy bonus BEFORE the game saves overnight to prevent compounding
            RemoveTemporaryEnergyBonus();
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // Handle Global Map Teleport Clicks (Fixed cursor issue by using GameMenu)
            if (isChoosingTeleportDestination && Game1.activeClickableMenu is StardewValley.Menus.GameMenu gameMenu && gameMenu.currentTab == 3) // 3 is the Map tab
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.ControllerA)
                {
                    var mapPage = gameMenu.pages[3];
                    string hoverText = this.Helper.Reflection.GetField<string>(mapPage, "hoverText").GetValue();

                    if (!string.IsNullOrWhiteSpace(hoverText))
                    {
                        var warpDest = GetWarpDestination(hoverText);
                        if (warpDest.HasValue)
                        {
                            Game1.warpFarmer(warpDest.Value.LocationName, warpDest.Value.X, warpDest.Value.Y, false);
                            Game1.exitActiveMenu();
                            isChoosingTeleportDestination = false;
                            Game1.playSound("wand");
                            Game1.addHUDMessage(new HUDMessage($"Teleported to {hoverText}.", HUDMessage.newQuest_type));
                        }
                        else
                        {
                            Game1.addHUDMessage(new HUDMessage($"Cannot teleport directly to {hoverText}. Try another region.", HUDMessage.error_type));
                        }
                    }
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
            }

            if (e.Button == SButton.MouseLeft || e.Button == SButton.ControllerX)
            {
                if (TryOneHitTree(e.Cursor.Tile, e.Cursor.GrabTile, Game1.player.GetGrabTile()))
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
            }

            if (e.Button == SButton.MouseRight || e.Button == SButton.ControllerA)
            {
                if (TryUseButcherKnife(e.Cursor.Tile, e.Cursor.GrabTile, Game1.player.GetGrabTile()))
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }

                if (TryActivateTeleporter(e.Cursor.Tile, e.Cursor.GrabTile, Game1.player.GetGrabTile()))
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }

                if (TryUseTimeCharm())
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }

                if (TryPlaceHeldItemOnConveyor(e.Cursor.Tile, e.Cursor.GrabTile, Game1.player.GetGrabTile()))
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
                        SpriteIndex = 797
                    };

                    data[ConveyorItemId] = new ObjectData
                    {
                        Name = "Conveyor",
                        DisplayName = "Conveyor",
                        Description = "A low belt that carries items to a machine at the end of its path.",
                        Type = "Crafting",
                        Category = StardewValley.Object.CraftingCategory,
                        Price = 100,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 71
                    };

                    data[TeleporterItemId] = new ObjectData
                    {
                        Name = "Teleporter",
                        DisplayName = "Teleporter",
                        Description = "Click it to open the map, then click a region to blink there instantly.",
                        Type = "Crafting",
                        Category = StardewValley.Object.CraftingCategory,
                        Price = 500,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 787
                    };

                    data[ButcherKnifeItemId] = new ObjectData
                    {
                        Name = "Butcher Knife",
                        DisplayName = "Butcher Knife",
                        Description = "Use on farm animals to turn them into raw meat.",
                        Type = "Crafting",
                        Category = StardewValley.Object.CraftingCategory,
                        Price = 1500,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 120
                    };

                    data[RawMeatItemId] = new ObjectData
                    {
                        Name = "Raw Meat",
                        DisplayName = "Raw Meat",
                        Description = "Fresh meat from a farm animal. Can be sold or cooked.",
                        Type = "Basic",
                        Category = StardewValley.Object.meatCategory,
                        Price = 75,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 684,
                        Edibility = 5
                    };

                    data[CookedMeatItemId] = new ObjectData
                    {
                        Name = "Cooked Meat",
                        DisplayName = "Cooked Meat",
                        Description = "A simple cooked cut of meat.",
                        Type = "Cooking",
                        Category = StardewValley.Object.CookingCategory,
                        Price = 160,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 214,
                        Edibility = 35
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
                    data["Conveyor"] = $"{StoneId} 20/Home/{ConveyorItemId}/false/default/";
                    data["Teleporter"] = $"{StoneId} 25 {WoodId} 10/Home/{TeleporterItemId}/false/default/";
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/CookingRecipes"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    data["Cooked Meat"] = $"{RawMeatItemId} 1/10/{CookedMeatItemId}/default/";
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ShopData>().Data;

                    foreach (var pair in data)
                    {
                        if (pair.Key.Contains("Animal", StringComparison.OrdinalIgnoreCase)
                            || pair.Key.Contains("Marnie", StringComparison.OrdinalIgnoreCase))
                        {
                            AddButcherKnifeToShop(pair.Value);
                        }
                    }
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
            {
                Game1.player.health = Math.Min(Game1.player.maxHealth, Game1.player.health + 20);
                Game1.addHUDMessage(new HUDMessage("Rainy overgrowth heals you.", HUDMessage.newQuest_type));
            }
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

            // Stop teleport process if the user closes the menu manually
            if (isChoosingTeleportDestination && (Game1.activeClickableMenu == null || (Game1.activeClickableMenu is StardewValley.Menus.GameMenu gm && gm.currentTab != 3)))
            {
                isChoosingTeleportDestination = false;
            }

            MakePlacedConveyorsPassable();

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

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.IsLocalPlayer)
                return;

            if (e.NewLocation.Name.Contains("Mine", StringComparison.OrdinalIgnoreCase))
                ApplyMineLuckBuff();
        }

        private void ApplyMineLuckBuff()
        {
            BuffEffects effects = new BuffEffects();
            effects.LuckLevel.Value = 3;
            effects.MiningLevel.Value = 1;

            Game1.player.applyBuff(new Buff(
                "adityagarg.NewLanguageProject_MineLuck",
                "New Language Project",
                "New Language Project",
                -2,
                null,
                -1,
                effects,
                false,
                "Mine Luck",
                "+3 luck and +1 mining while exploring the mines."
            ));

            Game1.addHUDMessage(new HUDMessage("The mines feel lucky today. Buff applied for the day!", HUDMessage.newQuest_type));
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

        private bool TryActivateTeleporter(params Vector2[] possibleTiles)
        {
            GameLocation location = Game1.player.currentLocation;

            foreach (Vector2 tile in possibleTiles)
            {
                if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && IsTeleporter(obj))
                {
                    isChoosingTeleportDestination = true;
                    // Opening the Map tab of the GameMenu ensures the mouse cursor is fully supported
                    Game1.activeClickableMenu = new StardewValley.Menus.GameMenu(3);
                        
                    Game1.addHUDMessage(new HUDMessage("Teleporter ready. Click a region on the map.", HUDMessage.newQuest_type));
                    return true;
                }
            }

            return false;
        }

        private (string LocationName, int X, int Y)? GetWarpDestination(string hoverText)
        {
            return hoverText.ToLowerInvariant() switch
            {
                var text when text.Contains("farm") => ("Farm", 64, 15),
                var text when text.Contains("town") => ("Town", 35, 35),
                var text when text.Contains("beach") => ("Beach", 20, 4),
                var text when text.Contains("forest") => ("Forest", 68, 16),
                var text when text.Contains("mountain") => ("Mountain", 15, 35),
                var text when text.Contains("desert") => ("Desert", 15, 40),
                var text when text.Contains("woods") => ("Woods", 27, 15),
                var text when text.Contains("quarry") => ("Mountain", 105, 12),
                var text when text.Contains("clinic") => ("Town", 35, 35),
                var text when text.Contains("shop") => ("Town", 35, 35),
                _ => null
            };
        }

        private bool TryUseButcherKnife(params Vector2[] possibleTiles)
        {
            if (Game1.player.CurrentItem?.ItemId != ButcherKnifeItemId)
                return false;

            FarmAnimal? animal = FindAnimalAtTiles(possibleTiles);

            if (animal is null)
            {
                Game1.addHUDMessage(new HUDMessage("No farm animal there.", HUDMessage.error_type));
                return true;
            }

            int meatAmount = GetMeatAmount(animal);
            string animalName = animal.displayName;

            if (!RemoveFarmAnimal(animal))
            {
                Game1.addHUDMessage(new HUDMessage("Could not butcher that animal.", HUDMessage.error_type));
                return true;
            }

            Game1.player.addItemToInventoryBool(new StardewValley.Object(RawMeatItemId, meatAmount));
            Game1.playSound("daggerswipe");
            Game1.addHUDMessage(new HUDMessage($"{animalName} became {meatAmount} raw meat.", HUDMessage.error_type));
            return true;
        }

        private FarmAnimal? FindAnimalAtTiles(IEnumerable<Vector2> possibleTiles)
        {
            HashSet<Vector2> tiles = new HashSet<Vector2>(possibleTiles);

            foreach (FarmAnimal animal in Game1.getFarm().getAllFarmAnimals())
            {
                Rectangle box = animal.GetBoundingBox();

                foreach (Vector2 tile in tiles)
                {
                    Point pixel = new Point((int)(tile.X * 64 + 32), (int)(tile.Y * 64 + 32));

                    if (box.Contains(pixel))
                        return animal;
                }
            }

            return null;
        }

        private static int GetMeatAmount(FarmAnimal animal)
        {
            string type = animal.type.Value;

            if (type.Contains("Chicken", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Duck", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Rabbit", StringComparison.OrdinalIgnoreCase))
                return 2;

            if (type.Contains("Cow", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Pig", StringComparison.OrdinalIgnoreCase)
                || type.Contains("Ostrich", StringComparison.OrdinalIgnoreCase))
                return 8;

            return 5;
        }

        private bool RemoveFarmAnimal(FarmAnimal animal)
        {
            long animalId = GetNetLongValue(animal, "myID");
            bool removed = false;

            foreach (GameLocation location in Game1.locations)
                removed |= RemoveAnimalFromObject(location, animalId);

            if (!removed)
            {
                foreach (GameLocation location in Game1.locations)
                {
                    foreach (FieldInfo field in location.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (!field.Name.Contains("animals", StringComparison.OrdinalIgnoreCase))
                            continue;

                        object? value = field.GetValue(location);

                        if (value is not null)
                            removed |= InvokeRemoveByLong(value, animalId);
                    }
                }
            }

            animal.Position = new Vector2(-9999, -9999);
            return removed || animalId != 0;
        }

        private static bool RemoveAnimalFromObject(object target, long animalId)
        {
            FieldInfo? field = target.GetType().GetField("animals", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object? animals = field?.GetValue(target);

            return animals is not null && InvokeRemoveByLong(animals, animalId);
        }

        private static bool InvokeRemoveByLong(object target, long key)
        {
            MethodInfo? remove = target.GetType().GetMethod("Remove", new[] { typeof(long) });

            if (remove is null)
                return false;

            return remove.Invoke(target, new object[] { key }) is bool result && result;
        }

        private bool TryOneHitTree(params Vector2[] possibleTiles)
        {
            if (Game1.player.CurrentTool is null || !Game1.player.CurrentTool.Name.Contains("Axe", StringComparison.OrdinalIgnoreCase))
                return false;

            GameLocation location = Game1.player.currentLocation;

            foreach (Vector2 tile in possibleTiles)
            {
                if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not Tree tree)
                    continue;

                tree.health.Value = 0;

                MethodInfo? performToolAction = feature.GetType().GetMethod(
                    "performToolAction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Tool), typeof(int), typeof(Vector2), typeof(GameLocation) },
                    null
                );

                if (performToolAction is not null)
                {
                    performToolAction.Invoke(feature, new object?[] { Game1.player.CurrentTool, 0, tile, location });
                    Game1.addHUDMessage(new HUDMessage("One-hit tree chop!", HUDMessage.newQuest_type));
                    return true;
                }
            }

            return false;
        }

        private bool TryPlaceHeldItemOnConveyor(params Vector2[] possibleTiles)
        {
            GameLocation location = Game1.player.currentLocation;
            Vector2? conveyorTile = FindClickedConveyorTile(location, possibleTiles);

            if (conveyorTile is null)
                return false;

            Item? heldItem = Game1.player.CurrentItem;

            if (heldItem is null)
            {
                Game1.addHUDMessage(new HUDMessage("Hold an item before placing it on the conveyor.", HUDMessage.error_type));
                return true;
            }

            if (heldItem.ItemId == ConveyorItemId)
                return false;

            ConveyorPath path = FindConveyorPath(location, conveyorTile.Value);

            if (path.Machines.Count == 0)
            {
                DrawConveyorPath(location, path.ConveyorTiles, Color.Yellow);
                Game1.addHUDMessage(new HUDMessage($"Conveyor path has {path.ConveyorTiles.Count} belt(s), but no machine at the end.", HUDMessage.error_type));
                return true;
            }

            foreach (StardewValley.Object machine in path.Machines)
            {
                Item oneItem = heldItem.getOne();
                oneItem.Stack = 1;

                if (!machine.performObjectDropInAction(oneItem, false, Game1.player))
                    continue;

                Game1.player.reduceActiveItemByOne();
                DrawConveyorPath(location, path.ConveyorTiles, Color.LimeGreen);
                Game1.addHUDMessage(new HUDMessage($"Conveyor sent {heldItem.DisplayName} through {path.ConveyorTiles.Count} belt(s).", HUDMessage.newQuest_type));
                return true;
            }

            DrawConveyorPath(location, path.ConveyorTiles, Color.Red);
            Game1.addHUDMessage(new HUDMessage("The machine at the end cannot use that item.", HUDMessage.error_type));
            return true;
        }

        private Vector2? FindClickedConveyorTile(GameLocation location, IEnumerable<Vector2> possibleTiles)
        {
            foreach (Vector2 tile in possibleTiles)
            {
                if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && IsConveyor(obj))
                    return tile;
            }

            return null;
        }

        private ConveyorPath FindConveyorPath(GameLocation location, Vector2 startTile)
        {
            Queue<Vector2> tilesToCheck = new Queue<Vector2>();
            HashSet<Vector2> checkedTiles = new HashSet<Vector2>();
            HashSet<Vector2> yieldedMachineTiles = new HashSet<Vector2>();
            List<Vector2> conveyorTiles = new List<Vector2>();
            List<StardewValley.Object> machines = new List<StardewValley.Object>();

            tilesToCheck.Enqueue(startTile);
            checkedTiles.Add(startTile);

            while (tilesToCheck.Count > 0)
            {
                Vector2 tile = tilesToCheck.Dequeue();
                conveyorTiles.Add(tile);

                foreach (Vector2 direction in AdjacentDirections)
                {
                    Vector2 nextTile = tile + direction;

                    if (!location.objects.TryGetValue(nextTile, out StardewValley.Object obj))
                        continue;

                    if (IsConveyor(obj))
                    {
                        if (checkedTiles.Add(nextTile))
                            tilesToCheck.Enqueue(nextTile);

                        continue;
                    }

                    if (obj is Chest)
                        continue;

                    if (yieldedMachineTiles.Add(nextTile))
                        machines.Add(obj);
                }
            }

            return new ConveyorPath(conveyorTiles, machines);
        }

        private static bool IsConveyor(StardewValley.Object obj)
        {
            return obj.ItemId == ConveyorItemId
                || obj.QualifiedItemId == $"(O){ConveyorItemId}"
                || obj.QualifiedItemId == $"(BC){ConveyorItemId}"
                || obj.Name == "Conveyor";
        }

        private static bool IsTeleporter(StardewValley.Object obj)
        {
            return obj.ItemId == TeleporterItemId
                || obj.QualifiedItemId == $"(O){TeleporterItemId}"
                || obj.Name == "Teleporter";
        }

        private void MakePlacedConveyorsPassable()
        {
            foreach (GameLocation location in Game1.locations)
            {
                foreach (var pair in location.objects.Pairs)
                {
                    if (!IsConveyor(pair.Value))
                        continue;

                    SetNetFieldValue(pair.Value, "isPassable", true);
                    pair.Value.boundingBox.Value = Rectangle.Empty;
                }
            }
        }

        private void DrawConveyorPath(GameLocation location, IEnumerable<Vector2> tiles, Color color)
        {
            foreach (Vector2 tile in tiles)
            {
                location.temporarySprites.Add(new TemporaryAnimatedSprite(10, tile * 64f, color, 1, false, 900f));
            }
        }

        private void AddButcherKnifeToShop(ShopData shop)
        {
            foreach (ShopItemData item in shop.Items)
            {
                if (item.ItemId == $"(O){ButcherKnifeItemId}" || item.ItemId == ButcherKnifeItemId)
                    return;
            }

            shop.Items.Add(new ShopItemData
            {
                Id = "adityagarg.NewLanguageProject_ButcherKnife",
                ItemId = $"(O){ButcherKnifeItemId}",
                Price = 1500,
                AvailableStock = 1
            });
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
            SetNetFieldValue(target, fieldName, value);
        }

        private static long GetNetLongValue(object target, string fieldName)
        {
            FieldInfo? field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field?.GetValue(target) is not object netField)
                return 0;

            PropertyInfo? valueProperty = netField.GetType().GetProperty("Value");
            object? value = valueProperty?.GetValue(netField);

            return value is long longValue ? longValue : 0;
        }

        private static void SetNetFieldValue(object target, string fieldName, object value)
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

        private sealed class ConveyorPath
        {
            public ConveyorPath(List<Vector2> conveyorTiles, List<StardewValley.Object> machines)
            {
                ConveyorTiles = conveyorTiles;
                Machines = machines;
            }

            public List<Vector2> ConveyorTiles { get; }
            public List<StardewValley.Object> Machines { get; }
        }
    }
}
