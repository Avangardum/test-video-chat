using System;
using System.Collections;
using System.Collections.Generic;
using agora_gaming_rtc;
using UnityEngine;
using UnityEngine.UI;

public class AgoraChat : MonoBehaviour
{
    [SerializeField] private string appID;
    [SerializeField] private string channelName;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject myViewGO;
    [SerializeField] private GameObject otherViewGO;

    private VideoSurface _myView;
    private VideoSurface _otherView;
    private IRtcEngine _rtcEngine;

    private void Awake()
    {
        SetupUI();
        SetupAgora();
    }
    
    private void SetupUI()
    {
        joinButton.onClick.AddListener(Join);
        leaveButton.onClick.AddListener(Leave);
        _myView = myViewGO.AddComponent<VideoSurface>();
        _otherView = otherViewGO.AddComponent<VideoSurface>();
        _myView.SetEnable(false);
        _otherView.SetEnable(false);
    }

    private void SetupAgora()
    {
        _rtcEngine = IRtcEngine.GetEngine(appID);
        _rtcEngine.OnUserJoined = OnUserJoined;
        _rtcEngine.OnUserOffline = OnUserOffline;
        _rtcEngine.OnJoinChannelSuccess = OnJoinChannelSuccess;
        _rtcEngine.OnLeaveChannel = OnLeaveChannel;
        _rtcEngine.OnError = OnError;
    }

    private void OnError(int error, string msg)
    {
        Debug.LogError($"Rtc engine error (id = {error}): {msg}");
    }

    private void OnLeaveChannel(RtcStats stats)
    {
        Debug.Log("Left channel");
        _myView.SetEnable(false);
        _otherView.SetEnable(false);
    }

    private void OnJoinChannelSuccess(string channelname, uint uid, int elapsed)
    {
        Debug.Log($"Joined channel {channelname} with id {uid}");
    }

    private void OnUserOffline(uint uid, USER_OFFLINE_REASON reason)
    {
        Debug.Log($"User {uid} disconnected");
        _otherView.SetEnable(false);
    }

    private void OnUserJoined(uint uid, int elapsed)
    {
        Debug.Log($"User {uid} joined");
        _otherView.SetForUser(uid);
        _otherView.SetEnable(true);
        // _otherView.SetVideoSurfaceType(AgoraVideoSurfaceType.RawImage);
        // _otherView.SetGameFps(30);
    }

    private void Join()
    {
        _rtcEngine.EnableVideo();
        _rtcEngine.EnableVideoObserver();
        _myView.SetEnable(true);
        _rtcEngine.JoinChannel(channelName, "", 0);
    }

    private void Leave()
    {
        _rtcEngine.LeaveChannel();
        _rtcEngine.DisableVideo();
        _rtcEngine.DisableVideoObserver();
    }

    private void OnApplicationQuit()
    {
        if (_rtcEngine != null)
        {
            IRtcEngine.Destroy();
            _rtcEngine = null;
        }
    }
}
