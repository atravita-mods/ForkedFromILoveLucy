﻿using Microsoft.Xna.Framework;
using StardewValley.Objects;

namespace FurnitureDisplayFramework
{
    public class FurnitureDisplayFrameworkAPI
    {
        public int GetTotalSlots(Furniture f)
        {
            var name = f.rotations.Value > 1 ? f.Name + ":" + f.currentRotation.Value : f.Name;
            if (!ModEntry.furnitureDisplayDict.ContainsKey(name))
                return 0;
            return ModEntry.furnitureDisplayDict[name].slots.Length;
        }
        public Rectangle? GetSlotRect(Furniture f, int i)
        {
            var name = f.rotations.Value > 1 ? f.Name + ":" + f.currentRotation.Value : f.Name;
            if (!ModEntry.furnitureDisplayDict.ContainsKey(name) || ModEntry.furnitureDisplayDict[name].slots.Length <= i)
                return null;
            var rect = ModEntry.furnitureDisplayDict[name].slots[i].slotRect;
            return new Rectangle?(new Rectangle(rect.X * 4, rect.Y * 4, rect.Width * 4, rect.Height * 4));
        }
    }
}