using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using agora_gaming_rtc;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AgoraChat : MonoBehaviour
{
    [SerializeField] private string channelName;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject myViewGO;
    [SerializeField] private GameObject[] otherViewGOs;
    [SerializeField] private TextMeshProUGUI usersInChannelText;
    [SerializeField] private GameObject homeScreen;
    [SerializeField] private GameObject chatScreen;
    [SerializeField] private CredentialStorage credentialStorage;
    [SerializeField] private GameObject channelFullWindow;

    private VideoSurface _myView;
    private VideoSurface[] _otherViews;
    private long[] _otherUserIDs;
    private IRtcEngine _rtcEngine;
    private int _otherUserCount;
    private int _userCount = -1;
    private Coroutine _showChannelFullMessageCoroutine;

    private int MaxUsers => _otherViews.Length + 1;

    private void Awake()
    {
        if (credentialStorage == null)
        {
            Debug.LogError("Credential storage is not assigned");
        }
        SetupUI();
        SetupAgora();
        _otherUserIDs = Enumerable.Repeat<long>(-1, _otherViews.Length).ToArray();
        StartCoroutine(UpdateUserCountCoroutine());
        
    }

    private void Update()
    {
        string prefix = "Users in channel: ";
        if (_userCount == -1)
        {
            usersInChannelText.text = prefix + "loading...";
        }
        else if (_userCount == -2)
        {
            usersInChannelText.text = prefix + "error";
        }
        else
        {
            usersInChannelText.text = prefix + $"{_userCount}/{MaxUsers}";
        }
    }

    private void SetupUI()
    {
        joinButton.onClick.AddListener(OnJoinButtonClick);
        leaveButton.onClick.AddListener(Leave);
        _myView = myViewGO.AddComponent<VideoSurface>();
        _otherViews = otherViewGOs.Select(x => x.AddComponent<VideoSurface>()).ToArray();
        _myView.SetEnable(false);
        foreach (var otherView in _otherViews)
            otherView.SetEnable(false);
    }

    private void SetupAgora()
    {
        _rtcEngine = IRtcEngine.GetEngine(credentialStorage.AgoraAppID);
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
        foreach (var otherView in _otherViews)
        {
            otherView.SetEnable(false);
        }
    }

    private void OnJoinChannelSuccess(string channelname, uint uid, int elapsed)
    {
        Debug.Log($"Joined channel {channelname} with id {uid}");
    }

    private void OnUserOffline(uint uid, USER_OFFLINE_REASON reason)
    {
        Debug.Log($"User {uid} disconnected");
        int userIndex = Array.IndexOf(_otherUserIDs, uid);
        _otherViews[userIndex].SetEnable(false);
        _otherUserIDs[userIndex] = -1;
    }

    private void OnJoinButtonClick()
    {
        if (_userCount < MaxUsers)
        {
            Join();
        }
        else
        {
            if(_showChannelFullMessageCoroutine != null) StopCoroutine(_showChannelFullMessageCoroutine);
            _showChannelFullMessageCoroutine = StartCoroutine(ShowChannelFullMessageCoroutine());
        }
    }
    
    private void OnUserJoined(uint uid, int elapsed)
    {
        Debug.Log($"User {uid} joined");
        int userIndex = Array.IndexOf(_otherUserIDs, -1);
        if (userIndex == -1)
        {
            throw new Exception("Too many users");
        }
        _otherUserIDs[userIndex] = uid;
        _otherViews[userIndex].SetForUser(uid);
        _otherViews[userIndex].SetEnable(true);
        // _otherView.SetVideoSurfaceType(AgoraVideoSurfaceType.RawImage);
        // _otherView.SetGameFps(30);
    }

    private void Join()
    {
        homeScreen.SetActive(false);
        chatScreen.SetActive(true);
        _rtcEngine.EnableVideo();
        _rtcEngine.EnableVideoObserver();
        _myView.SetEnable(true);
        _otherUserCount = 0;
        _rtcEngine.JoinChannel(channelName, "", 0);
    }

    private void Leave()
    {
        chatScreen.SetActive(false);
        homeScreen.SetActive(true);
        
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

    private IEnumerator UpdateUserCountCoroutine()
    {
        while (true)
        {
            string url = $"http://api.agora.io/dev/v1/channel/user/{credentialStorage.AgoraAppID}/{channelName}";
            string plainCredential =
                credentialStorage.AgoraRestfulAPICustomerID + ":" + credentialStorage.AgoraRestfulAPISecret;
            var plainTextBytes = Encoding.UTF8.GetBytes(plainCredential);
            string encodedCredential = Convert.ToBase64String(plainTextBytes);;
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", "Basic " + encodedCredential);
        
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string responseString = request.downloadHandler.text;
                Debug.Log(responseString);
                JObject response = JObject.Parse(responseString);
                var data = response["data"];
                bool doesChannelExist = data["channel_exist"].ToObject<bool>();
                _userCount = doesChannelExist ? data["total"].ToObject<int>() : 0;
            }
            else
            {
                _userCount = -2;
                string responseString;
                try
                {
                    responseString = request.downloadHandler.text;
                }
                catch (Exception e)
                {
                    responseString = "no response";
                }
                Debug.LogError($"Error while getting user count: {responseString}");
            }

            yield return new WaitForSeconds(0.1f);
        }
    }

    private IEnumerator ShowChannelFullMessageCoroutine()
    {
        channelFullWindow.SetActive(true);
        yield return new WaitForSeconds(2);
        channelFullWindow.SetActive(false);
    }
}
