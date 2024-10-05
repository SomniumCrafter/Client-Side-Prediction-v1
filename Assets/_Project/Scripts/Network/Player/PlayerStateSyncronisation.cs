using LindoNoxStudio.Network.Input;
using LindoNoxStudio.Network.Simulation;
using Unity.Netcode;
using UnityEngine;

namespace LindoNoxStudio.Network.Player
{
    public class PlayerStateSyncronisation : NetworkBehaviour
    {
        private const int StateBufferSize = 128; // 8 bit | 1 byte
        
        #if Client
        
        private PlayerState[] _predictedPlayerStates = new PlayerState[StateBufferSize];
        
        #elif Server
        
        private PlayerState[] _playerStates = new PlayerState[StateBufferSize];
        
        #endif
        
        private uint _latestStateTick;
        
        private PlayerController _playerController;

        public override void OnNetworkSpawn()
        {
            _playerController = GetComponent<PlayerController>();
        }

        public void SaveState(uint tick, ClientInputState input)
        {
            #if Client
            // Saving last state to compare with the server's state
            PlayerState state = _playerController.GetState(tick, input);
            _predictedPlayerStates[tick % StateBufferSize] = state;
            #elif Server
            // Saving last state to send to the client later
            PlayerState state = _playerController.GetState(tick, input);
            _playerStates[tick % StateBufferSize] = state;
            _latestStateTick = tick;
            #endif
        }

        #if Client
        
        bool wasWrong = false;
        private void HandleReconciliation(PlayerState serverState)
        {
            PlayerState clientState = _predictedPlayerStates[serverState.Tick % StateBufferSize];
            
            if (clientState == null)
            {
                Debug.Log("Something went wrong.");
                return;
            }
            else if (clientState.Tick != serverState.Tick)
            {
                Debug.Log("Something went wrong. " + clientState.Tick + " != " + serverState.Tick + " % " + (serverState.Tick - clientState.Tick) % StateBufferSize);
                wasWrong = true;
                return;
            }
            else
            {
                if (wasWrong)
                    Debug.Log("Nothing went wrong.");
                wasWrong = false;
            }

            if (Vector3.Distance(clientState.Position, serverState.Position) >= 0.001f)
            {
                Reconcile(serverState);
            }
            else if (Vector3.Distance(clientState.Rotation, serverState.Rotation) >= 0.001f)
            {
                Reconcile(serverState);
            }
            else 
            {
                Debug.Log("Prediction was correct.");
            }
        }

        private void Reconcile(PlayerState correctState)
        {
            Debug.Log("Prediction was not correct. ");
            // Save the state
            _predictedPlayerStates[correctState.Tick % StateBufferSize] = correctState;
            
            // Applying correct state
            ApplyState(_predictedPlayerStates[correctState.Tick % StateBufferSize]);
            
            // Use the input to predict the next states
            _playerController.OnInput(correctState.InputUsedForNextTick);
            
            for (uint tick = correctState.Tick + 1; tick < SimulationManager.CurrentTick + 1; tick++)
            {
                SimulationManager.HandlePhysicsTick(tick);
            }
        }

        private void ApplyState(PlayerState stateToApply)
        {
            // Applying the state on this client
            _playerController.ApplyState(stateToApply);

            // Todo: Apply state of other players
        }
        
        #elif Server
        
        public void SendState() 
        {
            PlayerState stateToSend = _playerStates[_latestStateTick % StateBufferSize];
            OnServerStateRPC(stateToSend, stateToSend.InputUsedForNextTick);
        } 
        
        #endif
        
        [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
        private void OnServerStateRPC(PlayerState playerState, ClientInputState inputForNextTick)
        {
            #if Client
            // Warning: Don't run this RPC unreliable, without changing this code down here!!!!!!!!!!!!!!!!!!!!!!!
            _latestStateTick = playerState.Tick;

            playerState.InputUsedForNextTick = inputForNextTick;
            HandleReconciliation(playerState);
            #endif
        }
    }
}