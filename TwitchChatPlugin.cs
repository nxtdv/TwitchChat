﻿using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace KingdomMod
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    [BepInProcess("KingdomTwoCrowns.exe")]
    public class TwitchChatPlugin : BasePlugin
    {
        public override void Load()
        {
            // Plugin startup logic
            Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");

            TwitchChat.Initialize(this);
        }
    }
}