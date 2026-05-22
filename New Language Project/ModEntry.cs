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
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using Microsoft.Xna.Framework.Graphics;
using StardewValley.GameData.Tools;

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
        private const string SteakItemId = "adityagarg.NewLanguageProject_Steak";
        private const string BaconItemId = "adityagarg.NewLanguageProject_Bacon";

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
        private bool wasEatingTimeCharm;

        private Chest? globalTargetChest;
        private bool isSelectingTargetInWorld;
        private bool isChoosingTeleportDestination;

        public override void Entry(IModHelper helper)
        {
            helper.Events.GameLoop.DayStarted += OnDayStarted;
            helper.Events.Player.InventoryChanged += OnInventoryChanged;
            helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.Player.Warped += OnWarped;
            helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
            helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }


        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            RemoveTemporaryEnergyBonus();

            shouldDropWindyItems = false;
            shouldGiveEnergyBonus = false;
            shouldApplyHeatPenalty = false;
            slowedTimeAdvancesRemaining = 0;
            shouldBlockNextTimeAdvance = false;
            wasEatingTimeCharm = false;

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
                Game1.addHUDMessage(new HUDMessage("Rainy overgrowth: crops may give extra yield today.",
                    HUDMessage.newQuest_type));

            if (isWindyDay)
            {
                Game1.addHUDMessage(new HUDMessage("Windy day: wood and seeds may appear around the farm.",
                    HUDMessage.newQuest_type));
                shouldDropWindyItems = true;
            }

            if (isHotDay)
            {
                shouldApplyHeatPenalty = true;
                Game1.addHUDMessage(new HUDMessage("Heat wave: stamina drains faster outdoors today.",
                    HUDMessage.error_type));
            }

            this.Monitor.Log("DayStarted event is running.", LogLevel.Info);
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return; 
            if (isSelectingTargetInWorld)
            {
                if (e.Button == SButton.MouseLeft || e.Button == SButton.ControllerX)
                {
                    Vector2 clickedTile = e.Cursor.Tile;

                    if (Game1.player.currentLocation.objects.TryGetValue(clickedTile, out StardewValley.Object obj) && obj is Chest clickedChest)
                    {
                        globalTargetChest = clickedChest;
                        isSelectingTargetInWorld = false;
                        Game1.playSound("drumkit6");
                        Game1.addHUDMessage(new HUDMessage("Target chest linked successfully!", HUDMessage.newQuest_type));
                    }
                    else
                    {
                        Game1.addHUDMessage(new HUDMessage("Must click a valid chest! Right-click to cancel.", HUDMessage.error_type));
                    }

                    this.Helper.Input.Suppress(e.Button);
                    return;
                }

                if (e.Button == SButton.MouseRight || e.Button == SButton.ControllerA)
                {
                    isSelectingTargetInWorld = false;
                    Game1.playSound("cancel");
                    Game1.addHUDMessage(new HUDMessage("Linking canceled.", HUDMessage.error_type));
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
            }
            if (Game1.activeClickableMenu is ItemGrabMenu grabMenu)
            {
                if ((e.Button == SButton.MouseLeft || e.Button == SButton.ControllerX) && grabMenu.organizeButton != null)
                {
                    int mouseX = Game1.getOldMouseX();
                    int mouseY = Game1.getOldMouseY();

                    Rectangle setButtonBounds = new Rectangle(
                        grabMenu.organizeButton.bounds.X - 76,
                        grabMenu.organizeButton.bounds.Y,
                        72,
                        64
                    );

                    if (setButtonBounds.Contains(mouseX, mouseY))
                    {
                        this.Helper.Input.Suppress(e.Button);
                        isSelectingTargetInWorld = true;
                        Game1.playSound("select");
                        Game1.exitActiveMenu();
                        Game1.addHUDMessage(new HUDMessage(
                            "Click on any chest in the world to set it as your target.",
                            HUDMessage.newQuest_type
                        ));
                        return;
                    }
                }

                if (e.Button == SButton.T)
                {
                    Item itemToTransfer = grabMenu.hoveredItem;

                    if (itemToTransfer != null)
                    {
                        this.Helper.Input.Suppress(e.Button);

                        if (globalTargetChest == null)
                        {
                            Game1.playSound("cancel");
                            Game1.addHUDMessage(new HUDMessage(
                                "No target chest linked yet! Click SET first.",
                                HUDMessage.error_type
                            ));
                            return;
                        }

                        if (grabMenu.context is Chest currentChest && currentChest == globalTargetChest)
                        {
                            Game1.playSound("cancel");
                            Game1.addHUDMessage(new HUDMessage(
                                "Cannot transfer items into the exact same chest.",
                                HUDMessage.error_type
                            ));
                            return;
                        }

                        Item? leftovers = globalTargetChest.addItem(itemToTransfer);

                        if (leftovers == null)
                        {
                            if (grabMenu.ItemsToGrabMenu.actualInventory.Contains(itemToTransfer))
                            {
                                int idx = grabMenu.ItemsToGrabMenu.actualInventory.IndexOf(itemToTransfer);
                                if (idx != -1) grabMenu.ItemsToGrabMenu.actualInventory[idx] = null;
                            }
                            else if (Game1.player.Items.Contains(itemToTransfer))
                            {
                                int idx = Game1.player.Items.IndexOf(itemToTransfer);
                                if (idx != -1) Game1.player.Items[idx] = null;
                            }

                            Game1.playSound("stoneStep");
                        }
                        else
                        {
                            itemToTransfer.Stack = leftovers.Stack;
                            Game1.playSound("stoneStep");
                            Game1.addHUDMessage(new HUDMessage(
                                "Target chest filled up completely!",
                                HUDMessage.error_type
                            ));
                        }
                        return;
                    }
                }
            }
            object? mapPageInstance = null;
            if (Game1.activeClickableMenu is GameMenu gm)
            {
                foreach (var page in gm.pages)
                {
                    if (page?.GetType().Name == "MapPage")
                    {
                        mapPageInstance = page;
                        break;
                    }
                }
            }
            else if (Game1.activeClickableMenu?.GetType().Name == "MapPage")
            {
                mapPageInstance = Game1.activeClickableMenu;
            }

            if (isChoosingTeleportDestination)
            {
                Game1.mouseCursorTransparency = 1f;
                if (mapPageInstance == null)
                    isChoosingTeleportDestination = false;
            }

            if (isChoosingTeleportDestination && mapPageInstance != null &&
                (e.Button == SButton.MouseLeft || e.Button == SButton.ControllerX))
            {
                FieldInfo? hoverTextField = mapPageInstance.GetType().GetField("hoverText",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                string? hoverText = hoverTextField?.GetValue(mapPageInstance) as string;

                string targetLocation = "";
                Vector2 targetTile = new Vector2(20, 20);

                if (!string.IsNullOrEmpty(hoverText))
                {
                    string text = hoverText.ToLower();
                    if (text.Contains("farm"))
                    {
                        targetLocation = "Farm";
                        targetTile = new Vector2(64, 30);
                    }
                    else if (text.Contains("secret woods") || text.Contains("woods"))
                    {
                        targetLocation = "Woods";
                        targetTile = new Vector2(20, 20);
                    }
                    else if (text.Contains("marnie") || text.Contains("leah") || text.Contains("wizard") ||
                             text.Contains("forest") || text.Contains("cindersap") || text.Contains("hat"))
                    {
                        targetLocation = "Forest";
                        targetTile = new Vector2(80, 20);
                    }
                    else if (text.Contains("desert") || text.Contains("oasis") || text.Contains("sandy"))
                    {
                        targetLocation = "Desert";
                        targetTile = new Vector2(25, 35);
                    }
                    else if (text.Contains("railroad") || text.Contains("spa") || text.Contains("bath") ||
                             text.Contains("witch"))
                    {
                        targetLocation = "Railroad";
                        targetTile = new Vector2(20, 45);
                    }
                    else if (text.Contains("mountain") || text.Contains("robin") || text.Contains("carpenter") ||
                             text.Contains("mine") || text.Contains("guild") || text.Contains("linus") ||
                             text.Contains("quarry"))
                    {
                        targetLocation = "Mountain";
                        targetTile = new Vector2(50, 20);
                    }
                    else if (text.Contains("beach") || text.Contains("willy"))
                    {
                        targetLocation = "Beach";
                        targetTile = new Vector2(40, 10);
                    }
                    else if (text.Contains("town") || text.Contains("pelican") || text.Contains("pierre") ||
                             text.Contains("saloon") || text.Contains("blacksmith") || text.Contains("museum") ||
                             text.Contains("joja") || text.Contains("clinic") || text.Contains("lewis"))
                    {
                        targetLocation = "Town";
                        targetTile = new Vector2(40, 40);
                    }
                }

                if (string.IsNullOrEmpty(targetLocation))
                {
                    Type menuType = mapPageInstance.GetType();
                    FieldInfo? xField = menuType.GetField("xPositionOnInterface",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo? yField = menuType.GetField("yPositionOnInterface",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo? wField = menuType.GetField("width",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo? hField = menuType.GetField("height",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                    int xPos = (int)(xField?.GetValue(mapPageInstance) ?? 0);
                    int yPos = (int)(yField?.GetValue(mapPageInstance) ?? 0);
                    int width = (int)(wField?.GetValue(mapPageInstance) ?? Game1.viewport.Width);
                    int height = (int)(hField?.GetValue(mapPageInstance) ?? Game1.viewport.Height);

                    int mapX = Game1.getMouseX() - xPos;
                    int mapY = Game1.getMouseY() - yPos;
                    float pctX = (float)mapX / width;
                    float pctY = (float)mapY / height;

                    if (pctX < 0.25f && pctY < 0.4f)
                    {
                        targetLocation = "Desert";
                        targetTile = new Vector2(25, 35);
                    }
                    else if (pctX < 0.35f && pctY >= 0.4f && pctY < 0.65f)
                    {
                        targetLocation = "Farm";
                        targetTile = new Vector2(64, 30);
                    }
                    else if (pctX < 0.35f && pctY >= 0.65f)
                    {
                        targetLocation = "Forest";
                        targetTile = new Vector2(80, 20);
                    }
                    else if (pctY < 0.3f)
                    {
                        if (pctX > 0.6f)
                        {
                            targetLocation = "Mountain";
                            targetTile = new Vector2(50, 20);
                        }
                        else
                        {
                            targetLocation = "Railroad";
                            targetTile = new Vector2(20, 45);
                        }
                    }
                    else if (pctY > 0.75f)
                    {
                        targetLocation = "Beach";
                        targetTile = new Vector2(40, 10);
                    }
                    else if (pctX > 0.7f && pctY < 0.5f)
                    {
                        targetLocation = "Mountain";
                        targetTile = new Vector2(90, 35);
                    }
                    else
                    {
                        targetLocation = "Town";
                        targetTile = new Vector2(40, 40);
                    }
                }

                if (!string.IsNullOrEmpty(targetLocation))
                {
                    isChoosingTeleportDestination = false;
                    Game1.exitActiveMenu();
                    Game1.warpFarmer(targetLocation, (int)targetTile.X, (int)targetTile.Y, 2);
                    Game1.playSound("wand");
                    Game1.addHUDMessage(new HUDMessage($"Teleported to {targetLocation}!", HUDMessage.newQuest_type));
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

                if (TryPlaceHeldItemOnConveyor(e.Cursor.Tile, e.Cursor.GrabTile, Game1.player.GetGrabTile()))
                {
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
            }


            if (e.Button == SButton.P)
                AutoPetAnimalsAndPets();
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ToolData>().Data;

                    data[ButcherKnifeItemId] = new ToolData
                    {
                        ClassName = "GenericTool",
                        Name = ButcherKnifeItemId,
                        DisplayName = "Butcher Knife",
                        Description = "Use on farm animals to turn them into raw meat.",
                        Texture = "TileSheets/weapons",
                        SpriteIndex = 4,
                        MenuSpriteIndex = 4,
                        CanBeLostOnDeath = false,
                        SetProperties = new Dictionary<string, string>
                        {
                            { "InstantUse", "true" },
                            { "IsEfficient", "true" }
                        }
                    };
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, ObjectData>().Data;

                    data[TimeCharmItemId] = new ObjectData
                    {
                        Name = "Time Charm",
                        DisplayName = "Time Charm",
                        Description = "Eat it to slow time for one in-game hour.",
                        Type = "Basic",
                        Category = 0,
                        Price = 100,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 797,
                        Edibility = 4
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
                        Description = "Opens the map menu. Click anywhere on the map to instantly teleport there.",
                        Type = "Crafting",
                        Category = StardewValley.Object.CraftingCategory,
                        Price = 500,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 787
                    };

                    data[RawMeatItemId] = new ObjectData
                    {
                        Name = "Raw Meat",
                        DisplayName = "Raw Meat",
                        Description = "Fresh meat from a farm animal. Highly profitable.",
                        Type = "Basic",
                        Category = StardewValley.Object.meatCategory,
                        Price = 300,
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
                        Price = 600,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 214,
                        Edibility = 35
                    };

                    data[SteakItemId] = new ObjectData
                    {
                        Name = "Steak",
                        DisplayName = "Steak",
                        Description = "A premium juicy cut of beef from a cow. Exceptionally valuable.",
                        Type = "Basic",
                        Category = StardewValley.Object.meatCategory,
                        Price = 1200,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 214,
                        Edibility = 40
                    };

                    data[BaconItemId] = new ObjectData
                    {
                        Name = "Bacon",
                        DisplayName = "Bacon",
                        Description = "Crispy, delicious strips of premium pork from a pig. Incredibly valuable.",
                        Type = "Basic",
                        Category = StardewValley.Object.meatCategory,
                        Price = 1500,
                        Texture = "Maps/springobjects",
                        SpriteIndex = 684,
                        Edibility = 30
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

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (Game1.player.itemToEat != null && Game1.player.itemToEat.ItemId == TimeCharmItemId)
            {
                wasEatingTimeCharm = true;
            }
            else if (wasEatingTimeCharm)
            {
                wasEatingTimeCharm = false;

                slowedTimeAdvancesRemaining = 6;
                shouldBlockNextTimeAdvance = true;
                Game1.addHUDMessage(new HUDMessage("Time bends around you for 1 in-game hour.",
                    HUDMessage.newQuest_type));
            }
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

            Game1.addHUDMessage(new HUDMessage("The wind scattered useful debris around the farm.",
                HUDMessage.newQuest_type));
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            MakePlacedConveyorsPassable();

            if (shouldGiveEnergyBonus)
            {
                shouldGiveEnergyBonus = false;
                temporaryEnergyBonus = pendingEnergyBonus;

                Game1.player.maxStamina.Value += temporaryEnergyBonus;
                Game1.player.Stamina = Game1.player.MaxStamina;
                Game1.player.modData[EnergyBonusKey] = temporaryEnergyBonus.ToString();

                Game1.addHUDMessage(new HUDMessage($"You woke up energized. +{pendingEnergyBonus} max energy today.",
                    HUDMessage.newQuest_type));
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
                120000,
                null,
                -1,
                effects,
                false,
                "Mine Luck",
                "+3 luck and +1 mining while exploring the mines."
            ));

            Game1.addHUDMessage(new HUDMessage("The mines feel lucky today.", HUDMessage.newQuest_type));
        }

        private bool TryActivateTeleporter(params Vector2[] possibleTiles)
        {
            bool isHoldingTeleporter = Game1.player.CurrentItem?.ItemId == TeleporterItemId ||
                                       Game1.player.CurrentItem?.Name == "Teleporter";
            bool isInteractingWithPlaced = false;

            GameLocation location = Game1.player.currentLocation;
            foreach (Vector2 tile in possibleTiles)
            {
                if (location.objects.TryGetValue(tile, out StardewValley.Object obj) && IsTeleporter(obj))
                {
                    isInteractingWithPlaced = true;
                    break;
                }
            }

            if (isHoldingTeleporter || isInteractingWithPlaced)
            {
                isChoosingTeleportDestination = true;
                Game1.activeClickableMenu = new GameMenu(GameMenu.mapTab);
                Game1.mouseCursorTransparency = 1f;
                Game1.addHUDMessage(new HUDMessage("Click anywhere on the map to teleport!", HUDMessage.newQuest_type));
                return true;
            }

            return false;
        }

        private bool TryUseButcherKnife(params Vector2[] possibleTiles)
        {
            if (Game1.player.CurrentItem is not Tool knife || knife.Name != ButcherKnifeItemId)
                return false;

            FarmAnimal? animal = FindAnimalAtTiles(possibleTiles);

            if (animal is null)
            {
                Game1.addHUDMessage(new HUDMessage("No farm animal there.", HUDMessage.error_type));
                return true;
            }

            var product = GetButcherProducts(animal);
            string animalName = animal.displayName;

            if (!RemoveFarmAnimal(animal))
            {
                Game1.addHUDMessage(new HUDMessage("Could not butcher that animal.", HUDMessage.error_type));
                return true;
            }

            string productName = "Meat";
            if (product.ItemId == SteakItemId) productName = "Steak";
            else if (product.ItemId == BaconItemId) productName = "Bacon";
            else if (product.ItemId == RawMeatItemId) productName = "Raw Meat";

            Game1.player.addItemToInventoryBool(new StardewValley.Object(product.ItemId, product.Amount));
            Game1.playSound("daggerswipe");
            Game1.addHUDMessage(new HUDMessage($"{animalName} became {product.Amount} {productName}.",
                HUDMessage.newQuest_type));
            return true;
        }

        private (string ItemId, int Amount) GetButcherProducts(FarmAnimal animal)
        {
            string type = animal.type.Value.ToLowerInvariant();

            if (type.Contains("cow")) return (SteakItemId, 25);
            if (type.Contains("pig")) return (BaconItemId, 15);
            if (type.Contains("chicken")) return (RawMeatItemId, 2);
            if (type.Contains("duck")) return (RawMeatItemId, 4);
            if (type.Contains("rabbit")) return (RawMeatItemId, 3);
            if (type.Contains("goat")) return (RawMeatItemId, 12);
            if (type.Contains("sheep")) return (RawMeatItemId, 10);
            if (type.Contains("ostrich")) return (RawMeatItemId, 20);

            return (RawMeatItemId, 5);
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
                    foreach (FieldInfo field in location.GetType()
                                 .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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
            FieldInfo? field = target.GetType().GetField("animals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
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
            if (Game1.player.CurrentTool is null ||
                !Game1.player.CurrentTool.Name.Contains("Axe", StringComparison.OrdinalIgnoreCase))
                return false;

            GameLocation location = Game1.player.currentLocation;

            foreach (Vector2 tile in possibleTiles)
            {
                if (!location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) || feature is not Tree tree)
                    continue;

                tree.health.Value = 0; // Forced 0 health for instant breakage

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
                Game1.addHUDMessage(new HUDMessage("Hold an item before placing it on the conveyor.",
                    HUDMessage.error_type));
                return true;
            }

            if (heldItem.ItemId == ConveyorItemId)
                return false;

            ConveyorPath path = FindConveyorPath(location, conveyorTile.Value);

            if (path.Machines.Count == 0)
            {
                DrawConveyorPath(location, path.ConveyorTiles, Color.Yellow);
                Game1.addHUDMessage(new HUDMessage(
                    $"Conveyor path has {path.ConveyorTiles.Count} belt(s), but no machine at the end.",
                    HUDMessage.error_type));
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
                Game1.addHUDMessage(new HUDMessage(
                    $"Conveyor sent {heldItem.DisplayName} through {path.ConveyorTiles.Count} belt(s).",
                    HUDMessage.newQuest_type));
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
                if (item.ItemId == $"(T){ButcherKnifeItemId}" || item.ItemId == ButcherKnifeItemId)
                    return;
            }

            shop.Items.Add(new ShopItemData
            {
                Id = "adityagarg.NewLanguageProject_ButcherKnife",
                ItemId = $"(T){ButcherKnifeItemId}",
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
                Game1.addHUDMessage(new HUDMessage($"Auto-petted {animalsPetted} animals and {petsPetted} pets.",
                    HUDMessage.newQuest_type));
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
            FieldInfo? field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field?.GetValue(target) is not object netField)
                return 0;

            PropertyInfo? valueProperty = netField.GetType().GetProperty("Value");
            object? value = valueProperty?.GetValue(netField);

            return value is long longValue ? longValue : 0;
        }

        private static void SetNetFieldValue(object target, string fieldName, object value)
        {
            FieldInfo? field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (field?.GetValue(target) is not object netField)
                return;

            PropertyInfo? valueProperty = netField.GetType().GetProperty("Value");
            valueProperty?.SetValue(netField, value);
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

        private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
        {
            if (isChoosingTeleportDestination)
                Game1.mouseCursorTransparency = 1f;

            if (!Context.IsWorldReady)
                return;

            if (Game1.activeClickableMenu is ItemGrabMenu grabMenu && grabMenu.organizeButton != null)
            {
                Game1.mouseCursorTransparency = 1f;
                int mouseX = Game1.getOldMouseX();
                int mouseY = Game1.getOldMouseY();

                Rectangle setButtonBounds = new Rectangle(
                    grabMenu.organizeButton.bounds.X - 76,
                    grabMenu.organizeButton.bounds.Y,
                    72,
                    64
                );

                IClickableMenu.drawTextureBox(
                    e.SpriteBatch,
                    Game1.mouseCursors,
                    new Rectangle(293, 360, 24, 24),
                    setButtonBounds.X,
                    setButtonBounds.Y,
                    setButtonBounds.Width,
                    setButtonBounds.Height,
                    setButtonBounds.Contains(mouseX, mouseY) ? Color.Wheat : Color.White,
                    4f,
                    false
                );

                Utility.drawTextWithShadow(
                    e.SpriteBatch,
                    "SET",
                    Game1.smallFont,
                    new Vector2(setButtonBounds.X + 16, setButtonBounds.Y + 16),
                    Game1.textColor
                );
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || !isSelectingTargetInWorld)
                return;

            Vector2 mouseTile = new Vector2(
                (Game1.getOldMouseX() + Game1.viewport.X) / 64,
                (Game1.getOldMouseY() + Game1.viewport.Y) / 64
            );

            Vector2 position = (mouseTile * 64f) - new Vector2(Game1.viewport.X, Game1.viewport.Y);

            GameLocation location = Game1.player.currentLocation;
            bool isValidChest = location.objects.TryGetValue(mouseTile, out StardewValley.Object obj) && obj is Chest;

            Color overlayColor = isValidChest ? Color.Lime * 0.4f : Color.Red * 0.4f;
            e.SpriteBatch.Draw(Game1.staminaRect, new Rectangle((int)position.X, (int)position.Y, 64, 64), overlayColor);
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