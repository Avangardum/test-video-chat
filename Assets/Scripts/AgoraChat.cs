using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using agora_gaming_rtc;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Networking;
using UnityEngine.UI;

public class AgoraChat : MonoBehaviour
{
    [SerializeField] private string channelName;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button leaveButton;
    [SerializeField] private GameObject myViewGO;
    [SerializeField] private GameObject[] otherViewGOs;
    [SerializeField] private Button[] muteButtons;
    [SerializeField] private TextMeshProUGUI usersInChannelText;
    [SerializeField] private GameObject homeScreen;
    [SerializeField] private GameObject chatScreen;
    [SerializeField] private CredentialStorage credentialStorage;
    [SerializeField] private GameObject channelFullWindow;
    [SerializeField] private GameObject pleaseWaitWindow;

    private VideoSurface _myView;
    private VideoSurface[] _otherViews;
    private long[] _otherUserIDs;
    private bool[] _areOtherUsersMuted;
    private Image[] _muteButtonImages;
    private IRtcEngine _rtcEngine;
    private int _otherUserCount;
    private int _userCount = -1;
    private Coroutine _showChannelFullMessageCoroutine;
    private Coroutine _showPleaseWaitMessageCoroutine;
    private float _timeUntilCanJoin;

    private int MaxUsers => _otherViews.Length + 1;

    private void Awake()
    {
        if (credentialStorage == null)
        {
            Debug.LogError("Credential storage is not assigned");
        }
        SetupUI();
        SetupAgora();
        GetPermissions();
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

        if (_timeUntilCanJoin > 0)
        {
            _timeUntilCanJoin -= Time.deltaTime;
        }
    }

    private void SetupUI()
    {
        joinButton.onClick.AddListener(OnJoinButtonClick);
        leaveButton.onClick.AddListener(Leave);
        _myView = myViewGO.AddComponent<VideoSurface>();
        _otherViews = otherViewGOs.Select(x => x.AddComponent<VideoSurface>()).ToArray();
        _muteButtonImages = muteButtons.Select(x => x.GetComponent<Image>()).ToArray();
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

    private void GetPermissions()
    {
        Permission.RequestUserPermissions(new []{Permission.Microphone, Permission.Camera});
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
        _timeUntilCanJoin = 5;
    }

    private void OnJoinChannelSuccess(string channelname, uint uid, int elapsed)
    {
        Debug.Log($"Joined channel {channelname} with id {uid}");
        _otherUserIDs = Enumerable.Repeat<long>(-1, _otherViews.Length).ToArray();
        _areOtherUsersMuted = Enumerable.Repeat(false, _otherViews.Length).ToArray();
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
        if (_timeUntilCanJoin > 0)
        {
            if (_showPleaseWaitMessageCoroutine != null) StopCoroutine(_showPleaseWaitMessageCoroutine);
            _showPleaseWaitMessageCoroutine = StartCoroutine(ShowPleaseWaitMessageCoroutine());
            return;
        }
        
        if (_userCount >= MaxUsers)
        {
            if(_showChannelFullMessageCoroutine != null) StopCoroutine(_showChannelFullMessageCoroutine);
            _showChannelFullMessageCoroutine = StartCoroutine(ShowChannelFullMessageCoroutine());
            return;
        }
        
        Join();
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
    
    private IEnumerator ShowPleaseWaitMessageCoroutine()
    {
        pleaseWaitWindow.SetActive(true);
        yield return new WaitForSeconds(2);
        pleaseWaitWindow.SetActive(false);
    }

    public void OnMuteButtonPress(int index)
    {
        if (_otherUserIDs[index] == -1)
            return;

        MuteUser(index, !_areOtherUsersMuted[index]);
    }

    public void MuteUser(int index, bool mute)
    {
        _areOtherUsersMuted[index] = mute;
        _muteButtonImages[index].color = mute ? new Color(.8f, .8f, .8f) : Color.white;
        _rtcEngine.AdjustUserPlaybackSignalVolume((uint)_otherUserIDs[index], mute ? 0 : 100);
    }
}
