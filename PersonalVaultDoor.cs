/*▄▄▄    ███▄ ▄███▓  ▄████  ▄▄▄██▀▀▀▓█████▄▄▄█████▓
▓█████▄ ▓██▒▀█▀ ██▒ ██▒ ▀█▒   ▒██   ▓█   ▀▓  ██▒ ▓▒
▒██▒ ▄██▓██    ▓██░▒██░▄▄▄░   ░██   ▒███  ▒ ▓██░ ▒░
▒██░█▀  ▒██    ▒██ ░▓█  ██▓▓██▄██▓  ▒▓█  ▄░ ▓██▓ ░ 
░▓█  ▀█▓▒██▒   ░██▒░▒▓███▀▒ ▓███▒   ░▒████▒ ▒██▒ ░ 
░▒▓███▀▒░ ▒░   ░  ░ ░▒   ▒  ▒▓▒▒░   ░░ ▒░ ░ ▒ ░░   
▒░▒   ░ ░  ░      ░  ░   ░  ▒ ░▒░    ░ ░  ░   ░    
 ░    ░ ░      ░   ░ ░   ░  ░ ░ ░      ░    ░      
 ░             ░         ░  ░   ░      ░  ░*/
using Oxide.Game.Rust.Cui;
using ProtoBuf;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("Personal Vault Door", "bmgjet", "1.0.3")]
    [Description("Lets you place a vault door")]
    public class PersonalVaultDoor : RustPlugin
    {
        [PluginReference]
        private Plugin ServerRewards, Economics;
        #region Configuration

        private Configuration config;

        private class Configuration
        {
            [JsonProperty("Vault Max Health")]
            public static float VaultMaxHealth = 2000;

            [JsonProperty(PropertyName = "Vault health name color")]
            public static string vaulthealthcolor = "#00FF00";

            [JsonProperty(PropertyName = "Range to show health")]
            public static float vaultrange = 3f;

            [JsonProperty("Repair Delay")]
            public float RepairDelay = 60;

            [JsonProperty("Repair Ammount")]
            public int RepairAmmount = 25;

            [JsonProperty("Spawn Cost type (serverrewards,economics,resources)")]
            public string Costtype = "resources";

            [JsonProperty("Repair Cost type (serverrewards,economics,resources)")]
            public string RepairCosttype = "resources";

            [JsonProperty("Spawn Cost for (serverrewards,economics)")]
            public float Spawncost = 2000;

            [JsonProperty("Repair Cost for (serverrewards,economics)")]
            public float Repaircost = 50;

            [JsonProperty("Currency Symbol for (serverrewards,economics)")]
            public string CurrencySymbol = "$";

            [JsonProperty("Charge to craft")]
            public bool craftcosts = true;

            [JsonProperty("Repair Cost")]
            //Items and quanity needed to repair
            public Dictionary<string, int> RepairCost = new Dictionary<string, int>
        {
                        {"scrap", 1},
                        {"metal.fragments", 100},
                        {"metal.refined", 10},
        };

            [JsonProperty("Craft Cost")]
            //Items and quanity needed per craft
            public Dictionary<string, int> CraftCost = new Dictionary<string, int>
        {
                        {"scrap", 10},
                        {"metal.fragments", 1000},
                        {"metal.refined", 100},
        };

            public string ToJson() => JsonConvert.SerializeObject(this);

            public Dictionary<string, object> ToDictionary() => JsonConvert.DeserializeObject<Dictionary<string, object>>(ToJson());
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null)
                {
                    throw new JsonException();
                }

                if (!config.ToDictionary().Keys.SequenceEqual(Config.ToDictionary(x => x.Key, x => x.Value).Keys))
                {
                    PrintWarning("Configuration appears to be outdated; updating and saving");
                    SaveConfig();
                }
            }
            catch
            {
                PrintWarning($"Configuration file {Name}.json is invalid; using defaults");
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            PrintWarning($"Configuration changes saved to {Name}.json");
            Config.WriteObject(config, true);
        }
        #endregion Configuration

        #region Vars
        //Offset from door slot position to move lock.
        Vector3 codeoffset = new Vector3(0.71f, -0.1f, -0.3f); //Puts it on flat pannel, On handles was too far for it to trigger on door still.

        //Skin of Icon
        private const ulong skinID = 2643584466;
        //Replacement prefab
        private const string prefab = "assets/bundled/prefabs/modding/asset_store/bankheist_package/bankheist_vol03/prefabs/door.vault.static.prefab";
        //Permission
        private const string permUse = "PersonalVaultDoor.use";
        //Show Debug Info
        private bool showDebug = false;
        //Sound Effects to play on vault break.
        static List<string> effects = new List<string>
        {
        "assets/bundled/prefabs/fx/entities/loot_barrel/gib.prefab",
        "assets/bundled/prefabs/fx/building/metal_sheet_gib.prefab"
        };
        private List<Vector3> REPlaced = new List<Vector3>();
        private static PersonalVaultDoor plugin;
        #endregion

        #region Language
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
            {"Name", "Vault Door"},
            {"Pickup", "You picked up Vault Door!"},
            {"Receive", "You received Vault Door!"},
            {"Repair", "You need more resources:\n{0}"},
            {"Wait", "You must wait: {0} before you can repair."},
            {"Permission", "You need permission to do that!"}
            }, this);
        }
        private static string HexToRustFormat(string hex)
        {
            Color color;
            return ColorUtility.TryParseHtmlString(hex, out color) ? $"{color.r:F2} {color.g:F2} {color.b:F2} {color.a:F2}" : "false";
        }
        //Send player message
        private void message(BasePlayer player, string key, params object[] args)
        {
            if (player == null) { return; }
            var message = string.Format(lang.GetMessage(key, this, player.UserIDString), args);
            player.ChatMessage(message);
        }
        #endregion

        #region Oxide Hooks
        private void OnServerInitialized(bool initial)
        {
            plugin = this;
            Fstartup(initial ? 30 : 1);
        }

        private void Fstartup(int delay)
        {
            //Wait to start up vault door componant for slow servers.
            timer.Once(delay, () =>
            {
                CheckVaultDoor();
            });
        }

        private void Init()
        {
            //Setup Permissions
            permission.RegisterPermission(permUse, this);
        }

        private void Unload()
        {
            //Clear Vault Door Health CUI
            foreach (BasePlayer player in BasePlayer.activePlayerList.ToArray())
            {
                CuiHelper.DestroyUi(player, "VaultHealth");
            }
            int VDS = DestroyVaultDoorScript();
            if (showDebug) Puts("Destroyed " + VDS.ToString() + " VaultDoor Scripts");
            //Unload Statics
            effects = null;
            plugin = null;
        }

        void OnEntityKill(Door entity)
        {
            //Check if vault door
            if (entity.ShortPrefabName == "door.vault.static")
            {
                VaultDoor VD = entity.GetComponent<VaultDoor>();
                if (VD == null) return;
                //Remove Door Frame
                NextTick(() =>
                {
                    if (VD.Frame != null)
                    {
                        VD.Frame.Kill();
                    }
                });
                if (VD.PlayersCUIed == null) return;
                //Remove CUI
                foreach (BasePlayer player in VD.PlayersCUIed)
                {
                    if (player != null)
                    {
                        CuiHelper.DestroyUi(player, "VaultHealth");
                    }
                }
                UnityEngine.Object.DestroyImmediate(VD);
            }
        }

        void OnEntitySpawned(CodeLock cl)
        {
            BaseEntity Door = cl.GetParentEntity();
            if (Door != null && Door.ToString().Contains("door.vault.static"))
            {
                //Moves codelock and update
                cl.transform.localPosition += codeoffset;
                cl.SendNetworkUpdateImmediate(true);
            }
        }

        void OnEntitySpawned(KeyLock cl)
        {
            BaseEntity Door = cl.GetParentEntity();
            if (Door != null && Door.ToString().Contains("door.vault.static"))
            {
                //Moves codelock and update
                cl.transform.localPosition += codeoffset;
                cl.SendNetworkUpdateImmediate(true);
            }
        }

        //Code and KeyLock position Fix
        void OnEntitySpawned(Door cl)
        {
            if (Rust.Application.isLoading) { return; }
            //Add component in spawn here so Copypaste can Work with vaultdoors.
            if (cl is Door && cl.ToString().Contains("door.vault.static"))
            {
                if (cl.GetComponent<VaultDoor>() == null)
                {
                    if (REPlaced.Contains(cl.transform.position))
                    {
                        if (showDebug) Puts("Skipping Rust Edit Placed Vault Door");
                        return;
                    }
                    if (showDebug) Puts("Adding VaultDoor Component");
                    //Delay since copypaste might not of spawn door frame yet.
                    timer.Once(2f, () =>
                    {
                        try
                        {
                            cl.gameObject.AddComponent<VaultDoor>();
                        }
                        catch { };
                    });
                }
            }
        }

        //Hook vault placement to switch in.
        private void OnEntityBuilt(Planner plan, GameObject go) { CheckDeploy(go.ToBaseEntity()); }

        //Hooks if should pickup
        private void OnHammerHit(BasePlayer player, HitInfo info) { CheckHit(player, info?.HitEntity); }
        #endregion

        #region Core
        List<Door> FindVaultDoors(Vector3 pos, float radius)
        {
            //Casts a sphere at given position and find all doors there
            var hits = Physics.SphereCastAll(pos, radius, Vector3.one);
            var x = new List<Door>();
            foreach (var hit in hits)
            {
                var entity = hit.GetEntity()?.GetComponent<Door>();
                if (entity && !x.Contains(entity))
                    x.Add(entity);
            }
            return x;
        }

        void DestroyGroundComp(BaseEntity ent)
        {
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<DestroyOnGroundMissing>());
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<GroundWatch>());
            //Stops Decay
            UnityEngine.Object.DestroyImmediate(ent.GetComponent<DeployableDecay>());
        }

        void DestroyMeshCollider(BaseEntity ent)
        {
            foreach (var mesh in ent.GetComponentsInChildren<MeshCollider>())
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        //Resets the component after server restart
        private void CheckVaultDoor()
        {
            //Build list of VaultDoors In Map File
            for (int i = World.Serialization.world.prefabs.Count - 1; i >= 0; i--)
            {
                PrefabData prefabdata = World.Serialization.world.prefabs[i];
                if (prefabdata.id == 3595032872)
                {
                    REPlaced.Add(prefabdata.position);
                    //Check its still there and fix if not
                    if (FindVaultDoors(prefabdata.position, 4f).Count == 0)
                    {
                        Door replacement = GameManager.server.CreateEntity(StringPool.Get(prefabdata.id), prefabdata.position, prefabdata.rotation) as Door;
                        if (replacement == null) return;
                        DestroyGroundComp(replacement);
                        DestroyMeshCollider(replacement);
                        replacement.Spawn();
                        replacement.transform.position = prefabdata.position;
                        replacement.transform.rotation = prefabdata.rotation;
                        replacement.pickup.enabled = false;
                        replacement.SendNetworkUpdateImmediate(true);
                    }
                }
            }
            if (showDebug) Puts("Founded " + REPlaced.Count.ToString() + " Map Placed Vault Doors");
            int VaultsUpdated = 0;
            foreach (var vaultdoor in GameObject.FindObjectsOfType<BaseEntity>())
            {
                //Skip Servers Vault Doors
                if (REPlaced.Contains(vaultdoor.transform.position))
                {
                    if (showDebug) Puts("Skipping Map Placed Vault Door @ " + vaultdoor.transform.position.ToString());
                    continue;
                }
                if (vaultdoor.ShortPrefabName == "door.vault.static" && vaultdoor.GetComponent<VaultDoor>() == null)
                {
                    if (showDebug) Puts("Found vaultdoor " + vaultdoor.ToString() + " " + vaultdoor.OwnerID.ToString() + " Adding Component");
                    vaultdoor.gameObject.AddComponent<VaultDoor>();
                    VaultsUpdated++;
                }
            }
            Puts("Updated " + VaultsUpdated.ToString() + " VaultDoors");
        }

        //Gives player vault door
        private void GiveVaultDoor(BasePlayer player, bool pickup = false)
        {
            var item = CreateItem();
            if (item != null && player != null)
            {
                player.GiveItem(item);
                message(player, pickup ? "Pickup" : "Receive");
            }
        }
        bool ChargePlayer(BasePlayer player)
        {
            object result = null;
            if (!config.craftcosts)
            {
                return true;
            }
            else
            {
                if (config.Costtype == "serverrewards" && ServerRewards != null)
                {
                    result = ServerRewards.Call("TakePoints", player.UserIDString, (int)config.Spawncost);
                }
                else if (config.Costtype == "economics" && Economics != null)
                {
                    result = Economics.Call("Withdraw", player.UserIDString, (double)config.Spawncost);
                }
                else
                {
                    // No supported rewards plugin loaded or configured
                    message(player, "Currency Type Not supported on this server");
                    return false;
                }
                if (result == null || (result is bool && (bool)result == false))
                {
                    message(player, "Looks like you can not afford to buy this");
                    return false;
                }
                message(player, "Charged {amount} {currency} for Vault Door".Replace("{amount}", config.Spawncost.ToString()).Replace("{currency}", config.CurrencySymbol));
                return true;
            }
        }

        bool CanaffordFix(BasePlayer player, Door VD)
        {
            object result = null;
            if (VD.SecondsSinceAttacked < config.RepairDelay)
            {
                message(player, "Wait", (config.RepairDelay - VD.SecondsSinceAttacked).ToString("#.#"));
                return false;
            }
            else
            {
                if (config.RepairCosttype == "serverrewards" && ServerRewards != null)
                {
                    result = ServerRewards.Call("TakePoints", player.UserIDString, (int)config.Repaircost);
                }
                else if (config.RepairCosttype == "economics" && Economics != null)
                {
                    result = Economics.Call("Withdraw", player.UserIDString, (double)config.Repaircost);
                }
                else
                {
                    // No supported rewards plugin loaded or configured
                    message(player, "Currency Type Not supported on this server");
                    return false;
                }
                if (result == null || (result is bool && (bool)result == false))
                {
                    message(player, "Looks like you can not afford to repair this");
                    return false;
                }
                message(player, "Charged {amount} {currency} for repairing Vault Door".Replace("{amount}", config.Repaircost.ToString()).Replace("{currency}", config.CurrencySymbol));
                return true;
            }
        }

        //Checks if has the correct permission
        private bool CanCraft(BasePlayer player)
        {
            if (!permission.UserHasPermission(player.UserIDString, permUse))
            {
                message(player, "Permission");
                return false;
            }
            return CanFix(player, null, true);
        }

        //Creates vault door
        private Item CreateItem()
        {
            var item = ItemManager.CreateByName("wall.frame.garagedoor", 1, skinID);
            if (item != null)
            {
                item.text = "Vault Door";
                item.name = item.text;
            }
            return item;
        }

        //Checks if its a vault door when hit with hammer
        private void CheckHit(BasePlayer player, BaseEntity entity)
        {
            if (entity == null) { return; }
            if (!IsVaultDoor(entity.skinID)) { return; }
            //Check if door is open and has no lock to remove Otherwise heal door
            Door VD = entity as Door;
            if (VD.GetSlot(0) == null && VD.IsOpen())
            {
                entity.GetComponent<VaultDoor>()?.TryPickup(player);
            }
            else
            {
                if (VD._health < VD._maxHealth)
                {
                    if (config.RepairCosttype != "resources")
                    {
                        if (CanaffordFix(player, VD))
                        {
                            Effect.server.Run("assets/bundled/prefabs/fx/build/repair_full_metal.prefab", VD.transform.position);
                            VD.health += config.RepairAmmount;
                        }
                    }
                    else
                    {
                        if (CanFix(player, VD))
                        {
                            Effect.server.Run("assets/bundled/prefabs/fx/build/repair_full_metal.prefab", VD.transform.position);
                            VD.health += config.RepairAmmount;
                        }
                    }
                }
                else
                {
                    message(player, "Vault Door Is full health");
                }
            }
        }

        //Checks if can fix
        private bool CanFix(BasePlayer player, Door VD, bool craft = false)
        {
            Dictionary<string, int> Needed = new Dictionary<string, int>();
            Dictionary<string, int> Parts = new Dictionary<string, int>();

            if (!craft)
            {
                Parts = config.RepairCost;
                //Check repair delay
                if (VD.SecondsSinceAttacked < config.RepairDelay)
                {
                    message(player, "Wait", (config.RepairDelay - VD.SecondsSinceAttacked).ToString("#.#"));
                    return false;
                }
            }
            else
            {
                if (!config.craftcosts) return true;
                Parts = config.CraftCost;
            }

            //Check has needed meterials
            foreach (var component in Parts)
            {
                string name = component.Key;
                if (player.inventory.GetAmount(ItemManager.FindItemDefinition(component.Key).itemid) < component.Value)
                {
                    if (!Needed.ContainsKey(name))
                    {
                        Needed.Add(name, 0);
                    }
                    Needed[name] += component.Value;
                }
            }
            //Has everything needed so remove from player and send can fix.
            if (Needed.Count == 0)
            {
                foreach (var item in Parts)
                {
                    player.inventory.Take(null, ItemManager.FindItemDefinition(item.Key).itemid, item.Value);
                }
                return true;
            }
            //Doesnt have everything needed to build list and message use. Send cant fix
            else
            {
                string text = "";
                foreach (var item in Needed)
                {
                    text += $" * {item.Key} x{item.Value}\n";
                }
                message(player, "Repair", text);
                return false;
            }
        }

        private bool IsVaultDoor(ulong skin) { return skin != 0 && skin == skinID; }
        //Checks if vault door should be swapped in
        private void CheckDeploy(BaseEntity entity)
        {
            if (entity == null) { return; }
            //Checks if is using vault door skin
            if (!IsVaultDoor(entity.skinID)) { return; }
            //Creates Doorway
            var doorway = GameManager.server.CreateEntity("assets/prefabs/building core/wall.doorway/wall.doorway.prefab", entity.transform.position, entity.transform.rotation);
            if (doorway == null)
            {
                //Something Failed
                return;
            }
            doorway.Spawn();
            doorway.OwnerID = entity.OwnerID;
            //sets up door frame to fill in gaps around vault door
            var buildingBlock = doorway as BuildingBlock;
            if (buildingBlock != null)
            {
                //Upgrade HQM
                buildingBlock.SetGrade((BuildingGrade.Enum)4);
                //Grounded so doesnt fall apart
                buildingBlock.grounded = false;
                //Sets health as max
                buildingBlock.health = buildingBlock.MaxHealth();
                //Rotate from Door Way
                Vector3 rot = buildingBlock.transform.rotation.eulerAngles;
                rot = new Vector3(rot.x, rot.y + 180, rot.z);
                //Create Vault Door
                Door vaultdoor = GameManager.server.CreateEntity(prefab, buildingBlock.transform.position, buildingBlock.transform.rotation) as Door;
                if (vaultdoor == null) { return; }
                //Stupid rotation based move stuff
                Vector3 movepos = buildingBlock.transform.position;
                movepos += buildingBlock.transform.forward * 1.0f;
                movepos += buildingBlock.transform.right * -0.54f;
                movepos += buildingBlock.transform.up * 0.2f;
                //Sets door way as creator so can destory vault on door way being destroyed
                vaultdoor.creatorEntity = doorway;
                vaultdoor.transform.rotation = Quaternion.Euler(rot);
                vaultdoor.transform.position = movepos;
                //Set skin and owner
                vaultdoor.skinID = skinID;
                vaultdoor.OwnerID = entity.OwnerID;
                //Delay setting Max health and health other wise it defaults to 800
                timer.Once(1f, () =>
                {
                    vaultdoor.SetMaxHealth(Configuration.VaultMaxHealth);
                    vaultdoor.SetHealth(Configuration.VaultMaxHealth);
                });
                //Sets up functions
                vaultdoor.Spawn();
                vaultdoor.SendNetworkUpdateImmediate();
            }
            //Cleans out placeholder
            NextTick(() => { entity?.Kill(); });
        }

        //Removes script on unload incase some ones restarting plugin but not restarted server
        int DestroyVaultDoorScript()
        {
            int killed = 0;
            foreach (var vaultdoor in GameObject.FindObjectsOfType<Door>())
            {
                foreach (var vd in vaultdoor.GetComponentsInChildren<VaultDoor>())
                {
                    UnityEngine.Object.DestroyImmediate(vd);
                    killed++;
                }
            }
            return killed;
        }
        #endregion

        #region Commands
        //Chat command
        [ChatCommand("vaultdoor")]
        private void Craft(BasePlayer player)
        {
            if (config.Costtype != "resources")
            {
                if (ChargePlayer(player)) { GiveVaultDoor(player); }
            }
            else
            {
                if (CanCraft(player)) { GiveVaultDoor(player); }
            }
        }

        //Console command
        [ConsoleCommand("vaultdoor.give")]
        private void Cmd(ConsoleSystem.Arg arg)
        {
            if (arg.IsAdmin && arg.Args?.Length > 0)
            {
                var player = BasePlayer.Find(arg.Args[0]) ?? BasePlayer.FindSleeping(arg.Args[0]);
                if (player == null)
                {
                    PrintWarning($"Can't find player with that name/ID! {arg.Args[0]}");
                    return;
                }
                GiveVaultDoor(player);
            }
        }
        #endregion

        #region Scripts
        public class VaultDoor : MonoBehaviour
        {
            //Hold doors info
            public Door vdoor = null;
            //Holds Position that can be used after Vdoors destroyed.
            public Vector3 Position;
            //Hold door frames info
            public BaseEntity Frame = null;
            //List of players that have CUI active
            public List<BasePlayer> PlayersCUIed = new List<BasePlayer>();
            private void Awake()
            {
                //Setup
                vdoor = this.GetComponent<Door>();
                if (vdoor == null) return;
                Position = vdoor.transform.position;
                //Set Custom Health
                vdoor.SetMaxHealth(Configuration.VaultMaxHealth);
                vdoor.SetHealth(Configuration.VaultMaxHealth);
                Frame = vdoor.creatorEntity;
                //If no creatorEntity most likey a server restart so find it.
                if (Frame == null)
                {
                    //Offsets scan position since where vault shows isnt where its position is.
                    Vector3 scanpos = Position;
                    scanpos += vdoor.transform.forward * 1.0f;
                    scanpos += vdoor.transform.right * -0.54f;
                    scanpos += vdoor.transform.up * 0.0f;
                    if (plugin.showDebug) foreach (BasePlayer BP in BasePlayer.activePlayerList) { if (BP.IsAdmin) BP.SendConsoleCommand("ddraw.sphere", 8f, Color.red, scanpos, 0.5f); }
                    List<BaseEntity> BuildingBlock = new List<BaseEntity>();
                    //Scans area for players
                    Vis.Entities<BaseEntity>(scanpos, 0.5f, BuildingBlock);
                    foreach (BaseEntity doorframe in BuildingBlock)
                    {
                        if (doorframe.ShortPrefabName == "wall.doorway")
                        {
                            if (plugin.showDebug) plugin.Puts("Found Creator Doorway " + doorframe.ToString());
                            Frame = doorframe;
                            break;
                        }
                    }
                }
                //Setup checking if door frame has been destroyed
                InvokeRepeating("CheckFrame", 5, 8);
                InvokeRepeating("VaultHealthBar", 1, 1);
            }

            void VaultHealthBar()
            {
                List<BasePlayer> PlayersInRange = new List<BasePlayer>();
                //Scans area for players
                Vis.Entities<BasePlayer>(vdoor.transform.position, Configuration.vaultrange, PlayersInRange);

                //Logic to remove CUI from players that have left area
                foreach (BasePlayer bp in PlayersCUIed.ToArray())
                {
                    if (!PlayersInRange.Contains(bp))
                    {
                        CuiHelper.DestroyUi(bp, "VaultHealth");
                        PlayersCUIed.Remove(bp);
                    }
                }

                //Shows CUI to each player in range
                if (PlayersInRange.Count != 0)
                {
                    foreach (BasePlayer player in PlayersInRange.ToArray())
                    {
                        if (!player.IsSleeping())
                        {
                            CuiHelper.DestroyUi(player, "VaultHealth");
                            var elements = new CuiElementContainer();
                            elements.Add(new CuiLabel { Text = { Text = "Vault Health " + vdoor._health.ToString("#.##") + "/" + vdoor._maxHealth.ToString(), FontSize = 16, Color = HexToRustFormat(Configuration.vaulthealthcolor), Align = TextAnchor.MiddleCenter }, RectTransform = { AnchorMin = "0.806 0.955", AnchorMax = "0.99 0.989" } }, "Overlay", "VaultHealth");
                            CuiHelper.AddUi(player, elements);
                            if (!PlayersCUIed.Contains(player))
                            {
                                PlayersCUIed.Add(player);
                            }
                        }
                    }
                }
            }

            void CheckFrame()
            {
                //Frame has been destoryed so destroy vault
                try
                {
                    if (Frame == null && vdoor != null)
                    {
                        foreach (var effect in effects) { Effect.server.Run(effect, Position); }
                        vdoor.Kill();
                    }
                }
                catch { }
            }

            public void TryPickup(BasePlayer player)
            {
                //Owner has hit with hammer, Destroy frame and vault door and refund them one.
                if (vdoor.OwnerID == player.userID)
                {
                    vdoor.Kill();
                    if (Frame != null)
                    {
                        Frame.Kill();
                    }
                    plugin.GiveVaultDoor(player, true);
                }
            }
        }
        #endregion
    }
}