using System;
using System.Collections.Generic;
namespace FarNet.ACD
{
    class CacheStorage
    {
        private static Dictionary<string, FSItem> itemsById = new Dictionary<string, FSItem>();
        private static Dictionary<string, FSItem> itemsByPath = new Dictionary<string, FSItem>();
        private static Dictionary<string, IList<FSItem>> itemsByParentPath = new Dictionary<string, IList<FSItem>>();

        public static FSItem GetItemById(string Id)
        {
#pragma warning disable 0162
            //return null;
#pragma warning restore 0162
            if (!itemsById.ContainsKey(Id))
            {
                return null;
            }

            if (itemsById[Id].IsExpired(60 * 15))
            {
                itemsById.Remove(Id); // expired
                return null;
            }

            return itemsById[Id];
        }

        public static FSItem GetItemByPath(string Path)
        {
#pragma warning disable 0162
            //return null;
#pragma warning restore 0162
            if (!itemsByPath.ContainsKey(Path))
            {
                return null;
            }

            if (itemsByPath[Path].IsExpired(60 * 15))
            {
                itemsByPath.Remove(Path); // expired
                return null;
            }

            return itemsByPath[Path];
        }

        public static IList<FSItem> GetItemsByParentPath(string ParentPath)
        {
#pragma warning disable 0162
            //return null;
#pragma warning restore 0162
            if (!itemsByParentPath.ContainsKey(ParentPath))
            {
                return null;
            }

            if (itemsByParentPath[ParentPath][0].IsExpired(60 * 15)) // if the first one is expired, then consider all as expired
            {
                itemsByParentPath.Remove(ParentPath); // expired
                return null;
            }

            return itemsByParentPath[ParentPath];
        }

        public static void RemoveItem(FSItem Item)
        {
            if (itemsById.ContainsKey(Item.Id))
            {
                itemsById.Remove(Item.Id);
            }

            if (itemsByPath.ContainsKey(Item.Path))
            {
                itemsByPath.Remove(Item.Path);
            }

            RemoveItems(Item.Dir); // invalidate parent dir
        }

        public static void RemoveItems(string ParentPath)
        {
            if (itemsByParentPath.ContainsKey(ParentPath))
            {
                itemsByParentPath.Remove(ParentPath);
            }
        }

        public static void AddItem(FSItem Item)
        {
            if (Item == null)
            {
                return;
            }

            try
            {
                itemsById.Add(Item.Id, Item);
            }
            catch (ArgumentException)
            {
                // If item exists, we replace it
                itemsById[Item.Id] = Item;
            }

            try
            {
                itemsByPath.Add(Item.Path, Item);
            }
            catch (ArgumentException)
            {
                // If item exists, we replace it
                itemsByPath[Item.Path] = Item;
            }
        }

        public static void AddItems(string parentPath, IList<FSItem> items)
        {
            try
            {
                itemsByParentPath.Add(parentPath, items);
            }
            catch (ArgumentException)
            {
                // If item exists, we replace it
                itemsByParentPath[parentPath] = items;
            }

            foreach (var item in items)
            {
                AddItem(item);
            }
        }
    }
}
