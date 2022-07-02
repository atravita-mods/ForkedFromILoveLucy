using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OverworldChests
{
    public class ModEntry : Mod
    {
        internal static ModEntry context;

        internal static ModConfig Config;
        private readonly List<string> niceNPCs = new();
        internal static IAdvancedLootFrameworkApi? advancedLootFrameworkApi = null;

        private Random myRand;
        private Color[] tintColors = new Color[]
        {
            Color.DarkGray,
            Color.Brown,
            Color.Silver,
            Color.Gold,
            Color.Purple,
        };
        private const string namePrefix = "Overworld Chest Mod Chest";
        private List<object> treasuresList;

        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            context = this;
            try
            {
                Config = this.Helper.ReadConfig<ModConfig>();
            }
            catch (Exception)
            {
                Config = new();
            }
            if (!Config.EnableMod)
                return;

            this.myRand = new Random();

            helper.Events.GameLoop.GameLaunched += this.GameLoop_GameLaunched;
            helper.Events.GameLoop.DayStarted += this.GameLoop_DayStarted;

            Harmony harmony = new(this.ModManifest.UniqueID);

            harmony.Patch(
                original: AccessTools.Method(typeof(Chest), nameof(Chest.draw), new Type[] { typeof(SpriteBatch), typeof(int), typeof(int), typeof(float) }),
                prefix: new HarmonyMethod(typeof(ModEntry), nameof(Chest_draw_Prefix))
            );
        }

        private void GameLoop_GameLaunched(object sender, StardewModdingAPI.Events.GameLaunchedEventArgs e)
        {
            if (context.Helper.ModRegistry.GetApi<IAdvancedLootFrameworkApi>("aedenthorn.AdvancedLootFramework") is IAdvancedLootFrameworkApi api)
            {
                this.treasuresList = api.LoadPossibleTreasures(Config.ItemListChances.Where(p => p.Value > 0).ToDictionary(s => s.Key, s => s.Value).Keys.ToArray(), Config.MinItemValue, Config.MaxItemValue);
                this.Monitor.Log($"Got {this.treasuresList.Count} possible treasures");
            }
        }

        private static bool Chest_draw_Prefix(Chest __instance)
        {
            if (!__instance.name.StartsWith(namePrefix))
                return true;

            if (!Game1.player.currentLocation.overlayObjects.ContainsKey(__instance.TileLocation) || (__instance.items.Count > 0 && __instance.items[0] != null) || __instance.coins.Value > 0)
                return true;

            context.Monitor.Log($"removing chest at {__instance.TileLocation}");
            Game1.player.currentLocation.overlayObjects.Remove(__instance.TileLocation);
            return false;
        }

        private void GameLoop_DayStarted(object sender, StardewModdingAPI.Events.DayStartedEventArgs e)
        {
            var spawn = this.Helper.Data.ReadSaveData<LastOverWorldChestSpawn>("lastOverworldChestSpawn") ?? new LastOverWorldChestSpawn();
            int days = Game1.Date.TotalDays - spawn.lastOverworldChestSpawn;
            this.Monitor.Log($"Last spawn: {days} days ago");
            if (spawn.lastOverworldChestSpawn < 1 || Game1.Date.TotalDays < 2 || (Config.RespawnInterval > 0 && days >= Config.RespawnInterval)) 
            {
                this.Monitor.Log($"Respawning chests", LogLevel.Debug);
                spawn.lastOverworldChestSpawn = Game1.Date.TotalDays;
                this.Helper.Data.WriteSaveData("lastOverworldChestSpawn", spawn);
                this.RespawnChests();
            }
        }

        private void RespawnChests()
        {
            Utility.ForAllLocations(delegate(GameLocation l)
            {
                if (l is FarmHouse || (!Config.AllowIndoorSpawns && !l.IsOutdoors) || !this.IsLocationAllowed(l))
                    return;

                this.Monitor.Log($"Respawning chests in {l.Name}");
                IList<Vector2> objectsToRemovePos = l.overlayObjects
                    .Where(o => o.Value is Chest && o.Value.Name.StartsWith(namePrefix))
                    .Select(o => o.Key)
                    .ToList();
                int rem = objectsToRemovePos.Count;
                foreach (var pos in objectsToRemovePos)
                {
                    l.overlayObjects.Remove(pos);
                }
                this.Monitor.Log($"Removed {rem} chests");
                int width = l.map.Layers[0].LayerWidth;
                int height = l.map.Layers[0].LayerHeight;
                bool IsValid(Vector2 v) => !l.isWaterTile((int)v.X, (int)v.Y) && !l.isTileOccupiedForPlacement(v) && !l.isCropAtTile((int)v.X, (int)v.Y);
                bool IsValidIndex(int i) => IsValid(new Vector2(i % width, i / width));
                int freeTiles = Enumerable.Range(0, width * height).Count(IsValidIndex);
                this.Monitor.Log($"Got {freeTiles} free tiles");
                int maxChests = Math.Min(freeTiles, (int)Math.Floor(freeTiles * Config.ChestDensity) + (Config.RoundNumberOfChestsUp ? 1 : 0));
                this.Monitor.Log($"Max chests: {maxChests}");
                while (maxChests-- > 0)
                {
                    Vector2 freeTile = l.getRandomTile();
                    if (!IsValid(freeTile))
                        continue;
                    Chest chest;
                    if (advancedLootFrameworkApi == null)
                    {
                        //Monitor.Log($"Adding ordinary chest");
                        chest = new Chest(0, new List<Item>() { MineShaft.getTreasureRoomItem() }, freeTile, false, 0);
                    }
                    else
                    {
                        double fraction = Math.Pow(this.myRand.NextDouble(), 1 / Config.RarityChance);
                        int level = (int)Math.Ceiling(fraction * Config.Mult);
                        //Monitor.Log($"Adding expanded chest of value {level} to {l.name}");
                        chest = advancedLootFrameworkApi.MakeChest(this.treasuresList, Config.ItemListChances, Config.MaxItems, Config.MinItemValue, Config.MaxItemValue, level, Config.IncreaseRate, Config.ItemsBaseMaxValue, Config.CoinBaseMin, Config.CoinBaseMax, freeTile);
                        chest.playerChoiceColor.Value = this.MakeTint(fraction);
                    }
                    chest.name = namePrefix;
                    l.overlayObjects[freeTile] = chest;
                }
            });
        }

        private bool IsLocationAllowed(GameLocation l)
        {
            if(Config.OnlyAllowLocations.Count > 0)
                return Config.OnlyAllowLocations.Contains(l.Name);
            return !Config.DisallowLocations.Contains(l.Name);
        }

        private Color MakeTint(double fraction)
            => this.tintColors[(int)(fraction * this.tintColors.Length)];
    }
}
