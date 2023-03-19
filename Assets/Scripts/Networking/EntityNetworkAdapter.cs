using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class EntityNetworkAdapter : NetworkBehaviour
{
    public struct ServerResponse : INetworkSerializable, IEquatable<ServerResponse>
    {
        public Vector3 position;
        public Vector3 velocity;
        public Quaternion rotation;
        public int faction;
        public float weaponGCDTimer;
        public float shell;
        public float core;
        public float energy;
        public int power;
        public ulong clientID;
        public ServerResponse(Vector3 position, Vector3 velocity, Quaternion rotation, ulong clientID, int faction, float weaponGCDTimer, int power, float shell, float core, float energy)
        {
            this.position = position;
            this.velocity = velocity;
            this.clientID = clientID;
            this.rotation = rotation;
            this.faction = faction;
            this.weaponGCDTimer = weaponGCDTimer;
            this.power = power;
            this.shell = shell;
            this.core = core;
            this.energy = energy;
        }

        public bool Equals(ServerResponse other)
        {
            return (clientID == other.clientID &&
                (this.position - other.position).sqrMagnitude > 1 &&
                (this.velocity - other.velocity).sqrMagnitude > 1 &&
                (this.rotation.eulerAngles - other.rotation.eulerAngles).sqrMagnitude > 1 &&
                this.faction == other.faction &&
                Mathf.Abs(this.weaponGCDTimer - other.weaponGCDTimer) > 0.1F &&
                this.power == other.power &&
                Mathf.Abs(this.shell - other.shell) > 0.5F &&
                Mathf.Abs(this.core - other.core) > 0.5F &&
                Mathf.Abs(this.energy - other.energy) > 0.5F);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref position);
            serializer.SerializeValue(ref velocity);
            serializer.SerializeValue(ref rotation);
            serializer.SerializeValue(ref clientID);
            serializer.SerializeValue(ref faction);
            serializer.SerializeValue(ref weaponGCDTimer);
            serializer.SerializeValue(ref power);
            serializer.SerializeValue(ref shell);
            serializer.SerializeValue(ref core);
            serializer.SerializeValue(ref energy);
        }
    }

    public string blueprintString;
    private EntityBlueprint blueprint;

    public struct PartStatusResponse : INetworkSerializable, IEquatable<PartStatusResponse>
    {
        public Vector2 location;
        public bool detached;

        public PartStatusResponse(Vector2 location, bool val)
        {
            this.location = location;
            detached = val;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref location);
            serializer.SerializeValue(ref detached);
        }

        public bool Equals(PartStatusResponse other)
        {
            return location == other.location;
        }
    }

    public NetworkList<PartStatusResponse> partStatuses;

    public struct ClientMessage : INetworkSerializable
    {
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            throw new System.NotImplementedException();
        }
    }


    public class TemporaryStateWrapper
    {
        public Vector3 position;
        public Vector3 directionalVector;
        public ulong clientID;

        public ServerResponse CreateResponse(EntityNetworkAdapter buf)
        {
            Rigidbody2D body = null;
            Entity core = null;
            body = buf.huskEntity.GetComponent<Rigidbody2D>();
            core = buf.huskEntity;
            return new ServerResponse(core.transform.position, 
            body.velocity, 
            core.transform.rotation, 
            clientID, 
            core.faction, 
            core.GetWeaponGCDTimer(),
            core as ShellCore ? (core as ShellCore).GetPower() : 0,
            core.CurrentHealth[0], 
            core.CurrentHealth[1], 
            core.CurrentHealth[2]);
        }
    }

    public TemporaryStateWrapper wrapper;

    public NetworkVariable<ServerResponse> state = new NetworkVariable<ServerResponse>();
    public NetworkVariable<bool> isPlayer = new NetworkVariable<bool>(false);
    public Vector3 pos;

    [SerializeField]
    private Entity huskEntity;
    public int passedFaction = 0;
    public int players = 0;

    void Awake()
    {
        if (partStatuses == null) partStatuses = new NetworkList<PartStatusResponse>();
        serverReady = new NetworkVariable<bool>(false);
    }
    void Start()
    {        
        if (NetworkManager.Singleton.IsServer)
        {
            if (isPlayer.Value)
            {
                Debug.LogWarning("TEST");
                players++;
            }
            if (passedFaction == 0 && isPlayer.Value) passedFaction = (players) % SectorManager.instance.GetFactionCount();
            if (IsOwner && isPlayer.Value) passedFaction = 0;
            state.Value = new ServerResponse(Vector3.zero, Vector3.zero, Quaternion.identity, OwnerClientId, passedFaction, 0, 0, 1000, 250, 500);
            blueprint = SectorManager.TryGettingEntityBlueprint(blueprintString);
        }
        if (NetworkManager.Singleton.IsClient)
        {
            state.OnValueChanged += (x, y) =>
            {
                if (y.clientID == NetworkManager.Singleton.LocalClientId && isPlayer.Value)
                {
                    UpdatePlayerState(y);
                }
                else if (huskEntity)
                {
                    UpdateCoreState(huskEntity, y);
                }
            };
        }
    }

    public override void OnNetworkSpawn()
    {
        if (wrapper == null)
        {
            wrapper = new TemporaryStateWrapper();
            wrapper.clientID = OwnerClientId;
        }

        if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.LocalClientId == OwnerClientId && isPlayer.Value)
        {
            PlayerCore.Instance.networkAdapter = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (MasterNetworkAdapter.mode == MasterNetworkAdapter.NetworkMode.Server || MasterNetworkAdapter.mode == MasterNetworkAdapter.NetworkMode.Host && isPlayer.Value)
        {
            MasterNetworkAdapter.instance.playerSpawned[OwnerClientId] = false;
        }
        ProximityInteractScript.instance.RemovePlayerName(huskEntity);
        if (huskEntity)
        {
            Destroy(huskEntity.gameObject);
        }

        if (MasterNetworkAdapter.mode != MasterNetworkAdapter.NetworkMode.Client && isPlayer.Value)
        {
            players--;
        }
    }

    private void UpdatePlayerState(ServerResponse response)
    {
        UpdateCoreState(PlayerCore.Instance, response);
        CameraScript.instance.Focus(PlayerCore.Instance.transform.position);
    }

    private void UpdateCoreState(Entity core, ServerResponse response)
    {
        core.transform.position = response.position;
        core.GetComponent<Rigidbody2D>().velocity = response.velocity;
        core.transform.rotation = response.rotation;
        core.dirty = false;
        core.SetWeaponGCDTimer(response.weaponGCDTimer);
        core.SyncHealth(response.shell, response.core, response.energy);
        if (core as ShellCore) (core as ShellCore).SyncPower(response.power);
    }
    
    [ServerRpc(RequireOwnership = false)]
    public void RequestDataStringsServerRpc(ServerRpcParams serverRpcParams = default)
    {
        GetDataStringsClientRpc(playerName, blueprintString);
    }

    [ClientRpc]
    public void GetDataStringsClientRpc(string name, string blueprint, ClientRpcParams clientRpcParams = default)
    {
        playerName = name;
        blueprintString = blueprint;
        this.blueprint = SectorManager.TryGettingEntityBlueprint(blueprint);
    }


    [ServerRpc(RequireOwnership = false)]
    public void RequestIDServerRpc(ServerRpcParams serverRpcParams = default)
    {
        GetIDClientRpc(idToUse);
    }


    [ClientRpc]
    public void GetIDClientRpc(string ID, ClientRpcParams clientRpcParams = default)
    {
        this.idToUse = ID;
        if (this.idToUse == "player" || isPlayer.Value) 
        {
            this.idToUse = "player-"+OwnerClientId;
        }
    }

    private ulong? tractorID;
    private bool queuedTractor = false;
    private bool dirty;


    [ServerRpc(RequireOwnership = false)]
    public void ForceNetworkVarUpdateServerRpc(ServerRpcParams serverRpcParams = default)
    {
        dirty = true;
    }
    public void SetTractorID(ulong? ID)
    {
        this.tractorID = ID;
        UpdateTractorClientRpc(ID.HasValue ? ID.Value : 0, !ID.HasValue);
    }

    [ServerRpc(RequireOwnership = true)]
    public void RequestTractorUpdateServerRpc(ulong id, bool setNull, ServerRpcParams serverRpcParams = default)
    {
        if (!isPlayer.Value || !huskEntity) return;
        if (setNull) 
        {
            (huskEntity as ShellCore).SetTractorTarget(null, true);
            SetTractorID(null);
        }
        else 
        {
            (huskEntity as ShellCore).SetTractorTarget(GetDraggableFromNetworkId(id), true);
            SetTractorID(id);
        }
    }

    public static Draggable GetDraggableFromNetworkId(ulong networkId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkId)) return null;
        var obj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkId];
        if (obj.GetComponent<Draggable>()) return obj.GetComponent<Draggable>();
        if (obj.GetComponent<EntityNetworkAdapter>() && obj.GetComponent<EntityNetworkAdapter>().huskEntity) 
            return obj.GetComponent<EntityNetworkAdapter>().huskEntity.GetComponent<Draggable>();
        return null;
    }

    public static bool TransformIsNetworked(Transform transform)
    {
        return transform && (transform.GetComponent<NetworkObject>() || transform.GetComponent<Entity>().networkAdapter);
    }

    public static ulong GetNetworkId(Transform transform)
    {
        if (!transform) return 0;
        var entity = transform.GetComponent<Entity>();
        ulong networkId = entity && entity.networkAdapter ? entity.networkAdapter.NetworkObjectId : 0;
        if (networkId == 0) networkId = transform.GetComponent<NetworkObject>() ? transform.GetComponent<NetworkObject>().NetworkObjectId : 0;
        return networkId;
    }

    [ClientRpc]
    public void UpdateTractorClientRpc(ulong ID, bool setNull, ClientRpcParams clientRpcParams = default)
    {
        if (MasterNetworkAdapter.mode != MasterNetworkAdapter.NetworkMode.Client) return;
        queuedTractor = true;
        tractorID = ID;
        if (setNull) tractorID = null;
    }

    public void ChangePositionWrapper(Vector3 newPos)
    {
        if (wrapper == null) wrapper = new TemporaryStateWrapper();
        wrapper.position = newPos;
    }



    [ServerRpc(RequireOwnership = true)]
    public void ChangeDirectionServerRpc(Vector3 directionalVector, ServerRpcParams serverRpcParams = default)
    {   
        if (OwnerClientId == serverRpcParams.Receive.SenderClientId)
        {
            wrapper.directionalVector = directionalVector;
        }
    }

    public static Ability GetAbilityFromLocation(Vector2 location, Entity core)
    {
        if (location == Vector2.zero)
        {
            return core.GetComponent<MainBullet>();
        }

        foreach (var part in core.NetworkGetParts())
        {            
            if (!part || part.info.location != location) continue;
            return part.GetComponent<Ability>();
        }
        return null;
    }


    [ServerRpc(RequireOwnership = true)]
    public void ExecuteAbilityServerRpc(Vector2 location, Vector3 victimPos, ServerRpcParams serverRpcParams = default)
    {   
        if (!huskEntity) return;
        var weapon = GetAbilityFromLocation(location, huskEntity);
        if (!weapon) return;
        weapon.Activate();
    }

    [ServerRpc(RequireOwnership = true)]
    public void ExecuteVendorPurchaseServerRpc(int index, ulong vendorID, ServerRpcParams serverRpcParams = default)
    {   
        if (!isPlayer.Value) return;
        if (!NetworkManager.SpawnManager.SpawnedObjects.ContainsKey(vendorID) ||
            !NetworkManager.SpawnManager.SpawnedObjects[vendorID].GetComponent<EntityNetworkAdapter>().huskEntity) return;
        var vendor = NetworkManager.SpawnManager.SpawnedObjects[vendorID].GetComponent<EntityNetworkAdapter>().huskEntity;
        if (!(vendor is IVendor)) return;
        VendorUI.BuyItem(huskEntity as ShellCore, index, vendor as IVendor);
    }

    [ClientRpc]
    public void ExecuteAbilityCosmeticClientRpc(Vector2 location, Vector3 victimPos)
    {
        if (NetworkManager.Singleton.IsServer) return;
        var core = huskEntity ? huskEntity : PlayerCore.Instance;
        if (!core) return;
        var weapon = GetAbilityFromLocation(location, core);
        if (weapon) weapon.ActivationCosmetic(victimPos);
    }
    public bool clientReady;

    public void ServerDetachPart(ShellPart part)
    {
        for (int i = 0; i < partStatuses.Count; i++)
        {
            if (partStatuses[i].location != part.info.location) continue;
            partStatuses[i] = new PartStatusResponse(part.info.location, true);
            break;
        }
    }


    public NetworkVariable<bool> serverReady;


    public void ServerResetParts()
    {
        for (int i = 0; i < partStatuses.Count; i++)
        {
            partStatuses[i] = new PartStatusResponse(partStatuses[i].location, true);
        }
    }

    public void SetHusk(Entity husk)
    {
        huskEntity = husk;
    }

    public string playerName;
    private bool playerNameAdded;
    private bool stringsRequested;
    public string idToUse;


    private bool PreliminaryStatusCheck()
    {
        if (!blueprint)
        {
            if (!stringsRequested)
            {
                RequestDataStringsServerRpc();
                RequestIDServerRpc();
                stringsRequested = true;
            }
            return false;
        }
        return true;
    }

    private void HandleQueuedTractor()
    {
        if (queuedTractor && huskEntity is ShellCore && MasterNetworkAdapter.mode == MasterNetworkAdapter.NetworkMode.Client)
        {
            NetworkObject nObj = null;
            if (tractorID.HasValue && !NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(tractorID.Value))
            {
                queuedTractor = false;
                tractorID = null;
                return;
            }
            if (tractorID.HasValue) nObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[tractorID.Value];
            if (tractorID.HasValue && (!nObj || !nObj.GetComponent<EntityNetworkAdapter>() || !nObj.GetComponent<EntityNetworkAdapter>().huskEntity)) return;
            queuedTractor = false;
            var core = huskEntity as ShellCore;
            if (tractorID == null)
            {
                core.SetTractorTarget(null, false, true);
            }
            else
            {
                core.SetTractorTarget(NetworkManager.Singleton.SpawnManager.SpawnedObjects[tractorID.Value].GetComponent<EntityNetworkAdapter>().huskEntity.GetComponentInChildren<Draggable>(), false, true);
            } 
        }
    }

    private void SetUpHuskEntity()
    {
        if ((!NetworkManager.IsClient || NetworkManager.Singleton.LocalClientId != OwnerClientId || (!isPlayer.Value && !string.IsNullOrEmpty(idToUse))) 
            && !huskEntity && SystemLoader.AllLoaded)
        {
            huskEntity = AIData.entities.Find(e => e.ID == idToUse);
            if (!huskEntity)
            {
                Sector.LevelEntity entity = new Sector.LevelEntity();
                var response = state;
                entity.faction = response.Value.faction;
                var print = Instantiate(blueprint);
                entity.ID = idToUse;
                entity.position = wrapper.position;
                var ent = SectorManager.instance.SpawnEntity(print, entity);
                if (MasterNetworkAdapter.mode != MasterNetworkAdapter.NetworkMode.Client)
                { 
                    GetIDClientRpc(entity.ID);
                }
                if (isPlayer.Value) 
                {
                    entity.ID = "player-"+OwnerClientId;
                }
                ent.husk = true;
                huskEntity = ent;
                huskEntity.blueprint = print;
                if (wrapper != null)
                {
                    huskEntity.spawnPoint = huskEntity.transform.position = wrapper.position;
                }
            }
            huskEntity.networkAdapter = this;
            clientReady = true;
            ForceNetworkVarUpdateServerRpc();
        }
        else if (NetworkManager.IsClient && NetworkManager.Singleton.LocalClientId == OwnerClientId && !clientReady && (serverReady.Value || NetworkManager.Singleton.IsHost) && (isPlayer.Value && !huskEntity))
        {
            var response = state;
            if (OwnerClientId == response.Value.clientID)
            {
                PlayerCore.Instance.faction = response.Value.faction;
                PlayerCore.Instance.blueprint = Instantiate(blueprint);
                if (!SystemLoader.AllLoaded && SystemLoader.InitializeCalled)
                {
                    SystemLoader.AllLoaded = true;
                    PlayerCore.Instance.StartWrapper();
                }
                else
                {    
                    PlayerCore.Instance.Rebuild();
                }
                PlayerCore.Instance.networkAdapter = this;
                idToUse = "player";
                huskEntity = PlayerCore.Instance;
                clientReady = true;
                ForceNetworkVarUpdateServerRpc();
            }
        }
        else if (NetworkManager.IsHost || (huskEntity && !huskEntity.GetIsDead()))
        {
            clientReady = true;
            if (MasterNetworkAdapter.mode == MasterNetworkAdapter.NetworkMode.Client)
            {
                var response = state;
                if (response.Value.faction != huskEntity.faction)
                {
                    huskEntity.faction = response.Value.faction;
                    huskEntity.Rebuild();
                }
            }
        }
    }

    private void AttemptCreateServerResponse()
    {
        if (NetworkManager.Singleton.IsServer)
        {
            var closeToPlayer = isPlayer.Value || !serverReady.Value;
            if (!closeToPlayer && huskEntity)
            {
                foreach(var ent in AIData.shellCores)
                {
                    if (ent && (ent.transform.position - huskEntity.transform.position).sqrMagnitude < MasterNetworkAdapter.POP_IN_DISTANCE)
                    {
                        closeToPlayer = true;
                        break;
                    }
                }
            }
            if ((huskEntity && closeToPlayer) || !serverReady.Value || dirty)
            {
                dirty = false;
                state.Value = wrapper.CreateResponse(this);
            };
        }
    }

    private void SyncUpParts()
    {
        if (NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsHost)
        {
            var core = huskEntity ? huskEntity : isPlayer.Value ? PlayerCore.Instance : null;
            if (!core || core.GetIsDead()) return;
            foreach (var part in partStatuses)
            {
                if (!part.detached) continue;
                var foundPart = core.NetworkGetParts().Find(p => p && p.info.location == part.location);
                if (!foundPart) continue;
                core.RemovePart(foundPart);
                break;
            }
        }
    }

    void Update()
    {   
        if (!PreliminaryStatusCheck()) return;
        HandleQueuedTractor();
        SetUpHuskEntity();
        AttemptCreateServerResponse();
        
        if (!playerNameAdded && !string.IsNullOrEmpty(playerName) && huskEntity as ShellCore && ProximityInteractScript.instance && isPlayer.Value)
        {
            playerNameAdded = true;
            ProximityInteractScript.instance.AddPlayerName(huskEntity as ShellCore, playerName);
        }
        if (huskEntity && huskEntity is Craft craft && craft.husk && isPlayer.Value)
        {
            craft.MoveCraft(wrapper.directionalVector);
        }

        SyncUpParts();
    }
}
