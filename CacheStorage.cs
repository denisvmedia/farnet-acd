using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FarNet.ACD
{
    class CacheItem
    {
        public FSItem Item;
        //DateTime expire;
    }

    class CacheStorage
    {
        private static Dictionary<string, CacheItem> items = new Dictionary<string, CacheItem>();

        public static FSItem GetItem(string Id)
        {
            if (!items.ContainsKey(Id))
            {
                return null;
            }
            
            return items[Id].Item;
        }

        public static void AddItem(FSItem item)
        {
            var cacheItem = new CacheItem() { Item = item };
            try
            {
                items.Add(item.Id, cacheItem);
            }
            catch (ArgumentException)
            {
                // If item exists, we replace it
                items[item.Id] = cacheItem;
            }
        }
    }
}
