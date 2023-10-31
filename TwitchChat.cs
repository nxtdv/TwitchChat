using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace KingdomMod
{
    public class TwitchChat : MonoBehaviour
    {
        private static ManualLogSource log;

        // Twitch Configuration
        private string username = "justinfan5555";
        private string password = "kappa";
        private string channelName = "";
        private TcpClient twitchClient;
        private StreamReader reader;
        private StreamWriter writer;

        // Game and player status
        private bool enabledObjectsInfo = true;
        private HashSet<GameObject> farmers = new HashSet<GameObject>();
        private Dictionary<string, GameObject> userToFarmerMapping = new Dictionary<string, GameObject>();
        private Queue<string> joinQueue = new Queue<string>();
        private object queueLock = new object();
        private List<string> randomNames = new List<string> { "John", "Emma", "Liam", "Olivia", "Noah", "Ava", "Ethan", "Isabella", "Lucas", "Mia" };
        private readonly List<ObjectsInfo> objectsInfoList = new List<ObjectsInfo>();
        private static readonly string FarmerTag = Tags.Farmer;
        private Dictionary<GameObject, string> farmerNames = new Dictionary<GameObject, string>();

        // User interface
        private bool showTwitchUsernameModal = true;
        private string twitchUsername = "";
        private Rect modalRect = new Rect(100, 100, 300, 150);
        private bool guiInteractionComplete = false;
        private readonly GUIStyle guiStyle = new GUIStyle();


        public static void Initialize(TwitchChatPlugin plugin)
        {
            log = plugin.Log;
            var component = plugin.AddComponent<TwitchChat>();
            component.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(component.gameObject);
        }

        private void Update()
        {
            if (!IsPlaying()) return;
            if (Input.GetKeyDown(KeyCode.F5))
            {
                enabledObjectsInfo = !enabledObjectsInfo;
            }

            if (twitchClient == null || !twitchClient.Connected)
            {
                if (channelName != "")
                {
                    ConnectToTwitch();
                }

                return;
            }

            ProcessJoinQueue();
        }

        private void OnGUI()
        {
            if (!IsPlaying()) return;

            AssignNamesToNewFarmers();

            if (enabledObjectsInfo)
            {
                UpdateObjectsInfo();
                DrawObjectsInfo();
            }

            if (!guiInteractionComplete)
            {
                if (showTwitchUsernameModal)
                {
                    DrawTwitchUsernameModal();
                    SetCursorVisibility(true);
                }
                else
                {
                    SetCursorVisibility(false);
                }
            }
        }

        private void DrawTwitchUsernameModal()
        {
            float labelHeight = 50f;
            float fontSize = 25f;
            float labelWidth = Screen.width;

            float xPos = (Screen.width - labelWidth);
            float yPos = 20f;

            GUI.skin.label.fontSize = (int)fontSize;
            GUI.skin.label.alignment = TextAnchor.MiddleCenter;

            GUI.Label(new Rect(xPos, yPos, labelWidth, labelHeight), "If a menu appears while you are entering your nickname, finish writing and validate, then press escape to close the menu.");


            modalRect = GUI.Window(0, modalRect, (GUI.WindowFunction)TwitchUsernameModalWindow, "Enter your Twitch username");
        }

        private void TwitchUsernameModalWindow(int windowID)
        {
            float modalWidth = 300f;
            float modalHeight = 150f;
            float buttonWidth = 100f;
            float buttonHeight = 20f;

            float xPos = (Screen.width - modalWidth) / 2f;
            float yPos = 90f;

            modalRect = new Rect(xPos, yPos, modalWidth, modalHeight);

            GUI.SetNextControlName("TwitchUsernameField");
            twitchUsername = GUI.TextField(new Rect(10, 40, 280, 20), twitchUsername);

            if (GUI.Button(new Rect(10, 70, buttonWidth, buttonHeight), "Cancel"))
            {
                showTwitchUsernameModal = false;
                guiInteractionComplete = true;

            }

            GUI.enabled = !string.IsNullOrEmpty(twitchUsername);
            if (GUI.Button(new Rect(modalWidth - buttonWidth - 10, 70, buttonWidth, buttonHeight), "Confirm"))
            {
                channelName = twitchUsername;
                showTwitchUsernameModal = false;
                guiInteractionComplete = true;
            }

            GUI.enabled = true;
            GUI.FocusControl("TwitchUsernameField");
        }

        private void SetCursorVisibility(bool visible)
        {
            var cursorSystem = GameObject.FindObjectOfType<CursorSystem>();

            if (cursorSystem)
            {
                cursorSystem.SetForceVisibleCursor(visible);
            }
            else
            {
                Cursor.visible = visible;
                Cursor.lockState = visible ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }

        private void AssignNamesToNewFarmers()
        {
            var farmersArray = GameObject.FindGameObjectsWithTag(FarmerTag);
            var availableFarmer = farmersArray.Length > 0;

            if (!availableFarmer)
            {
                return;
            }

            foreach (var farmer in farmersArray)
            {
                if (!farmers.Any(existingFarmer => existingFarmer == farmer))
                {
                    string randomName = randomNames[UnityEngine.Random.Range(0, randomNames.Count)];
                    farmerNames[farmer] = randomName;
                    farmers.Add(farmer);
                    log.LogInfo($"New farmer : {farmer.name} assigned random name: {randomName}");
                }
            }
        }

        private void UpdateObjectsInfo()
        {
            if (!IsPlaying() || IsInSettings()) return;

            var worldCam = Managers.Inst.game._mainCameraComponent;
            if (worldCam == null) return;

            objectsInfoList.Clear();

            var screenHeight = Screen.height;

            foreach (var farmer in farmers)
            {
                if (farmer == null || !farmer.activeSelf) continue;

                if (!farmerNames.TryGetValue(farmer, out var farmerName)) continue;

                var renderer = farmer.GetComponent<Renderer>();
                if (renderer != null && renderer.isVisible)
                {
                    Vector3 screenPos = worldCam.WorldToScreenPoint(farmer.transform.position);
                    float uiPosY = screenHeight - screenPos.y - 50;

                    if (screenPos.x > 0 && screenPos.x < Screen.width && uiPosY > 0 && uiPosY < screenHeight)
                    {
                        objectsInfoList.Add(new ObjectsInfo(new Rect(screenPos.x, uiPosY, 100, 100), farmer.transform.position, farmerName));
                    }
                }
            }
        }

        private void DrawObjectsInfo()
        {
            guiStyle.normal.textColor = Color.white;
            float yOffset = 2f;
            float stackingDistance = 5f;

            objectsInfoList.Sort((a, b) => a.Pos.y.CompareTo(b.Pos.y));

            for (int i = 0; i < objectsInfoList.Count; i++)
            {
                var obj = objectsInfoList[i];
                Vector2 textSize = guiStyle.CalcSize(new GUIContent(obj.Info));
                float labelWidth = textSize.x;
                float labelHeight = textSize.y;

                float centerX = obj.Pos.x - labelWidth / 2f;
                float aboveHeadY = obj.Pos.y - labelHeight * yOffset;

                for (int j = 0; j < i; j++)
                {
                    var existingObj = objectsInfoList[j];
                    float existingLabelWidth = guiStyle.CalcSize(new GUIContent(existingObj.Info)).x;
                    float existingLabelHeight = guiStyle.CalcSize(new GUIContent(existingObj.Info)).y;
                    float existingCenterX = existingObj.Pos.x - existingLabelWidth / 2f;
                    float existingAboveHeadY = existingObj.Pos.y - existingLabelHeight * yOffset;

                    Rect labelRect = new Rect(centerX, aboveHeadY, labelWidth, labelHeight);
                    Rect existingLabelRect = new Rect(existingCenterX, existingAboveHeadY, existingLabelWidth, existingLabelHeight);

                    if (labelRect.Overlaps(existingLabelRect))
                    {
                        aboveHeadY = existingLabelRect.y - labelHeight - stackingDistance;
                        labelRect.y = aboveHeadY;
                    }
                }

                GUI.Label(new Rect(centerX, aboveHeadY, labelWidth, labelHeight), obj.Info, guiStyle);
            }

            objectsInfoList.Clear();
        }

        private void ReadChatContinuous()
        {
            while (twitchClient.Connected)
            {
                string message = reader.ReadLine();
                if (message != null && message.Contains("PRIVMSG"))
                {
                    int splitPoint = message.IndexOf("!", 1);
                    string chatName = message.Substring(1, splitPoint - 1);

                    splitPoint = message.IndexOf(":", 1);
                    string chatMessage = message.Substring(splitPoint + 1);

                    if (chatMessage.Equals("!join", StringComparison.OrdinalIgnoreCase) && !userToFarmerMapping.ContainsKey(chatName) && joinQueue.Count < 150)
                    {
                        joinQueue.Enqueue(chatName);
                    }
                    else if (joinQueue.Count >= 150) continue;
                }
            }
        }

        private void ProcessJoinQueue()
        {
            lock (queueLock)
            {
                while (joinQueue.Count > 0 && farmers.Any(farmer => !userToFarmerMapping.ContainsValue(farmer)))
                {
                    string chatName = joinQueue.Dequeue();
                    GameObject availableFarmer = farmers.FirstOrDefault(farmer => !userToFarmerMapping.ContainsValue(farmer));

                    if (availableFarmer != null)
                    {
                        userToFarmerMapping[chatName] = availableFarmer;
                        string randomName = chatName;
                        farmerNames[availableFarmer] = randomName;
                        log.LogInfo($"Assigning username {randomName} to Farmer {availableFarmer.name}");
                    }
                }
            }
        }

        private void ConnectToTwitch()
        {
            twitchClient = new TcpClient("irc.chat.twitch.tv", 6667);
            reader = new StreamReader(twitchClient.GetStream());
            writer = new StreamWriter(twitchClient.GetStream());

            writer.WriteLine("PASS " + password);
            writer.WriteLine("NICK " + username);
            writer.WriteLine("USER " + username + " 8 * :" + username);
            writer.WriteLine("JOIN #" + channelName);
            writer.Flush();

            Thread chatThread = new Thread(ReadChatContinuous);
            chatThread.Start();
        }

        private bool IsPlaying()
        {
            var game = Managers.Inst?.game;
            return game != null && (game.state is Game.State.Playing or Game.State.NetworkClientPlaying or Game.State.Menu);
        }

        private bool IsInSettings()
        {
            var game = Managers.Inst?.game;
            return game != null && game.state == Game.State.Menu;
        }

        private void OnDestroy()
        {
            if (twitchClient != null && twitchClient.Connected)
            {
                twitchClient.Close();
            }
        }

        public class ObjectsInfo
        {
            public Rect Pos;
            public Vector3 Vec;
            public string Info;

            public ObjectsInfo(Rect pos, Vector3 vec, string info)
            {
                this.Pos = pos;
                this.Vec = vec;
                this.Info = info;
            }
        }
    }
}
