-- Force-enables the user "Auto End Turn" option at game load. The engine
-- ignores this option in multiplayer/hotseat regardless (the MP gating
-- is handled by ActionPanel.lua's override), but for single-player it
-- saves the user a trip into Game Options. Persists to Options.ini.

local function ForceAutoEndTurn()
    if UserConfiguration == nil then return end
    if UserConfiguration.IsAutoEndTurn() then return end

    UserConfiguration.SetValue("AutoEndTurn", 1)
    if Options ~= nil then
        Options.SetUserOption("Gameplay", "AutoEndTurn", 1)
        if Options.SaveOptions ~= nil then
            Options.SaveOptions()
        end
    end
    print("civ6-async: AutoEndTurn enabled")
end

ForceAutoEndTurn()
Events.LoadScreenClose.Add(ForceAutoEndTurn)
