﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Lean.Touch;
using UnityEngine.EventSystems;
using Photon.Pun;

[Serializable]
public class FlashlightToggledEvent : UnityEvent<FlashState> { }
[Serializable]
public class GameStateToggledEvent : UnityEvent<GameState> { }

public class GameController : MonoBehaviour
{
    private static readonly string GAME_OBJECT_TAG = "GameModel";
    private static readonly string OBJECT_SELECTION_TAG = "ModelSelection";

    [SerializeField]
    private TrackableType trackable;

    private GameState gameState = GameState.Preparing;
    private FlashState flashState = FlashState.Off;

    public FlashlightToggledEvent flashlightToggleEvent { get; private set; } = new FlashlightToggledEvent();
    public GameStateToggledEvent gameStateToggledEvent { get; private set; } = new GameStateToggledEvent();

    [SerializeField]
    private GameObject gun;
    [SerializeField]
    private GameObject blood;
    [SerializeField]
    private GameObject knife;
    [SerializeField]
    private GameObject bullet;
    [SerializeField]
    private GameObject footprints;
    [SerializeField]
    private GameObject fingerprint;
    [SerializeField]
    private GameObject body;
    [SerializeField]
    private GameObject hair;

    private GameObject objectToSpawn = null;
    private GameObject selectedObject = null;
    private HashSet<GameObject> hiddenObjects = new HashSet<GameObject>();

    [SerializeField]
    private GameObject playButton;
    [SerializeField]
    private GameObject flashButton;
    [SerializeField]
    private GameObject finishButton;
    [SerializeField]
    private GameObject objectModalButton;

    private ARSessionOrigin arSessionOrigin;
    private ARRaycastManager arRaycastManager;

    void Awake()
    {
        arSessionOrigin = FindObjectOfType<ARSessionOrigin>();
        arRaycastManager = arSessionOrigin.GetComponent<ARRaycastManager>();
    }

    void Start()
    {
        var networkController = GameObject.FindObjectOfType<NetworkController>();
        networkController.leftRoomEvent.AddListener(OnLeftRoom);
        networkController.joinRoomEvent.AddListener(OnJoinRoom);
    }

    public void OnFingerTap(LeanFinger finger)
    {
        if (gameState == GameState.Playing)
        {
            return;
        }

        var hits = new List<ARRaycastHit>();
        if (arRaycastManager.Raycast(finger.ScreenPosition, hits, trackable))
        {
            var pose = hits[0].pose;

            RaycastHit hit;
            if (Physics.Raycast(finger.GetRay(), out hit) && hit.collider.CompareTag(GAME_OBJECT_TAG))
            {
                return;
            }

            var photonGameObject = PhotonNetwork.Instantiate(objectToSpawn.name, pose.position, pose.rotation);

            var selectionController = photonGameObject.GetComponent<SelectionController>();
            selectionController.leanSelectedEvent.AddListener(OnSelectObject);
            selectionController.leanDeselectedEvent.AddListener(OnDeselectObject);
        }
    }

    private void ToggleHideableGameObject(GameObject gameObject, bool hide)
    {
        var hideableDecorator = gameObject.GetComponent<HideableDecorator>();
        if (hideableDecorator.isHideable)
        {
            hideableDecorator.isHidden = hide;
            gameObject.SetActive(!hide);

            if (hide)
            {
                hiddenObjects.Add(gameObject);
            }
            else
            {
                hiddenObjects.Remove(gameObject);
            }
        }
    }

    [PunRPC]
    private void ToggleHideableGameObjects(bool hide)
    {
        var allGameObjects = new List<GameObject>();

        allGameObjects.AddRange(GameObject.FindGameObjectsWithTag(GAME_OBJECT_TAG));
        allGameObjects.AddRange(hiddenObjects);

        foreach (var gameObject in allGameObjects.ToArray())
        {
            ToggleHideableGameObject(gameObject, hide);
        }
    }

    public void OnGameReadyTap()
    {
        gameState = GameState.Playing;
        gameStateToggledEvent.Invoke(gameState);

        playButton.SetActive(false);
        flashButton.SetActive(true);
        finishButton.SetActive(true);
        objectModalButton.SetActive(false);

        HideSelectionOutlines();

        ToggleHideableGameObjects(true);

        if (PhotonNetwork.IsMasterClient)
        {
            var photonView = PhotonView.Get(this);
            photonView.RPC("ToggleHideableGameObjects", RpcTarget.Others, new object[] { true });
        }
    }

    public void OnFinishTap()
    {
        var gameObjects = GameObject.FindGameObjectsWithTag(GAME_OBJECT_TAG);
        foreach (var gameObject in gameObjects)
        {
            var photonView = gameObject.GetComponent<PhotonView>();
            if (photonView.IsMine)
            {
                PhotonNetwork.Destroy(photonView);
            }
            else
            {
                gameObject.SetActive(false);
                Destroy(gameObject, 1f);
            }
        }

        gameState = GameState.Finishing;
        gameStateToggledEvent.Invoke(gameState);
        flashState = FlashState.Off;
        flashlightToggleEvent.Invoke(flashState);
    }

    private void OnLeftRoom()
    {
        playButton.SetActive(true);
        flashButton.SetActive(false);
        finishButton.SetActive(false);
        objectModalButton.SetActive(true);

        gameState = GameState.Preparing;
        gameStateToggledEvent.Invoke(gameState);
    }

    private void OnJoinRoom()
    {
        if (!PhotonNetwork.IsMasterClient)
        {
            OnGameReadyTap();
        }
    }

    public void OnFlashlightTap()
    {
        switch (flashState)
        {
            case FlashState.Off:
                flashState = FlashState.On;
                break;
            case FlashState.On:
            default:
                flashState = FlashState.Off;
                break;
        }

        flashlightToggleEvent.Invoke(flashState);

        ToggleHideableGameObjects(!FlashlightUtils.FlashStateToBool(flashState));
    }

    private void OnSelectObject(LeanFinger finger)
    {
        RaycastHit hit;
        if (Physics.Raycast(finger.GetRay(), out hit) && hit.collider.CompareTag(GAME_OBJECT_TAG))
        {
            var gameObject = hit.collider.gameObject;
            var outline = gameObject.GetComponent<Outline>();
            outline.OutlineMode = Outline.Mode.OutlineAll;
            selectedObject = gameObject;
        }
    }

    private void OnDeselectObject()
    {
        var outline = selectedObject.GetComponent<Outline>();
        outline.OutlineMode = Outline.Mode.OutlineHidden;
        selectedObject = null;
    }

    private void HideSelectionOutlines()
    {
        var items = GameObject.FindGameObjectsWithTag(OBJECT_SELECTION_TAG);

        foreach (var item in items)
        {
            var outline = item.GetComponentInChildren<UnityEngine.UI.Outline>();
            if (outline != null)
            {
                outline.enabled = false;
            }
        }
    }

    private void ShowSelectionOutline()
    {
        var current = EventSystem.current.currentSelectedGameObject;
        var outline = current.GetComponentInChildren<UnityEngine.UI.Outline>();
        if (outline != null)
        {
            outline.enabled = true;
        }
    }
    private void HandleSelectMenuItem(Action action)
    {
        HideSelectionOutlines();
        ShowSelectionOutline();
        action.Invoke();
    }

    public void OnGunSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = gun);
    }

    public void OnBloodSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = blood);
    }

    public void OnKnifeSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = knife);
    }

    public void OnBulletSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = bullet);
    }

    public void OnFootprintSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = footprints);
    }

    public void OnBodySelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = body);
    }

    public void OnFingerprintSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = fingerprint);
    }

    public void OnHairSelect()
    {
        HandleSelectMenuItem(() => objectToSpawn = hair);
    }
}
