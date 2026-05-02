----------------------------------------------------------------
-- PlayerChange (modded by civ6-async)
--
-- Replacement for Base/Assets/UI/Popups/PlayerChange.lua. Two changes:
--   1. OnLocalPlayerTurnBegin: when the incoming hotseat player has no
--      password, skip the "Click to begin your turn" popup by invoking
--      the same close path the OK button would.
--   2. OnLocalPlayerTurnEnd: suppress the "Please Wait" popup queue in
--      hotseat. Engine state flags are still set; only the UI is gone.
----------------------------------------------------------------
include("PopupPriorityLoader_", true);

local PopupTitleSuffix = Locale.Lookup( "LOC_PLAYER_CHANGE_POPUP_TITLE_SUFFIX" );
local bPlayerChanging :boolean = false;
local bLocalPlayerTurnEnded :boolean = false;
local bLocalPlayerDestroyed :boolean = false;


function OnInputHandler( uiMsg, wParam, lParam )
	if uiMsg == KeyEvents.KeyUp then
		if wParam == Keys.VK_RETURN then
			OnKeyUp_Return();
		elseif wParam == Keys.VK_ESCAPE then
			OnMenu();
		end
	end
	return true;
end

function OnKeyUp_Return()
	if (not Controls.PopupAlphaIn:IsHidden()) then
		local localPlayerID = Game.GetLocalPlayer();
		local localPlayer = PlayerConfigurations[localPlayerID];
		if(localPlayer ~= nil
			and localPlayer:GetHotseatPassword() == "") then
			OnOk();
		end
	end
end

function OnSave()
	UIManager:QueuePopup(Controls.SaveGameMenu, PopupPriority.PlayerChange);
end

function OnOk()
	EffectsManager:ResumeAllEffects();
	if(GameConfiguration.IsHotseat()) then
		SetPause(false);
	end
	LuaEvents.PlayerChange_Close(Game.GetLocalPlayer());
	UIManager:DequeuePopup( ContextPtr );
	Controls.PopupAlphaIn:SetHide(true);
	Controls.PopupAlphaIn:SetToBeginning();
	Controls.PopupSlideIn:SetToBeginning();
end

function OnCancelUpload()
	UIManager:SetUICursor( 1 );
	UITutorialManager:EnableOverlay( false );
	UITutorialManager:HideAll();
	UIManager:Log("Shutting down via player change exit-to-main-menu.");
	Events.ExitToMainMenu();
end

function OnMenu()
	LuaEvents.PlayerChange_OpenInGameOptionsMenu();
end

-- ===========================================================================
-- MODIFIED: when there's no hotseat password we never queue the popup;
-- instead invoke OnOk directly so the same close events fire and the game
-- unpauses. Password-protected players still get the prompt.
-- ===========================================================================
function OnLocalPlayerTurnBegin()
	bLocalPlayerTurnEnded = false;
	if(GameConfiguration.IsHotseat() == true) then
		bPlayerChanging = false;
		local localPlayerID = Game.GetLocalPlayer();
		local localPlayer = PlayerConfigurations[localPlayerID];
		if(localPlayer ~= nil and localPlayer:GetHotseatPassword() == "") then
			OnOk();
		else
			BuildTurnControls();
		end
	end
end

function OnLocalPlayerTurnEnd()
	if not bLocalPlayerDestroyed then
		bLocalPlayerTurnEnded = true;
		bPlayerChanging = true;
		if(GameConfiguration.IsHotseat()) then
			SetPause(false);
			-- civ6-async: skip the "Please Wait" popup in hotseat so FireTuner
			-- stays connected through the player handoff. State flags above
			-- are still set; only the UI queue is suppressed.
			return;
		end
		UIManager:QueuePopup( ContextPtr, PopupPriority.PlayerChange);
	end
end

function GetNumAliveHumanPlayers()
	local aPlayers = PlayerManager.GetAliveMajors();
	local numAliveHumanPlayers = 0;
	for _, pPlayer in ipairs(aPlayers) do
		if(pPlayer:IsHuman()) then
			numAliveHumanPlayers = numAliveHumanPlayers + 1;
		end
	end
	return numAliveHumanPlayers;
end

function BuildTurnControls()
	print("BuildTurnControls: CurrentGameTurn=" .. Game.GetCurrentGameTurn());
	if(GameConfiguration.IsHotseat()) then
		SetPause(true);
		local localPlayerID = Game.GetLocalPlayer();
		local localPlayer = PlayerConfigurations[localPlayerID];
		Controls.TitleText:SetText(Locale.ToUpper(localPlayer:GetPlayerName()));
	end

	if(ContextPtr:IsHidden()) then
		UIManager:QueuePopup( ContextPtr, PopupPriority.PlayerChange);
	else
		ShowTurnControls();
	end
end

-- ===========================================================================
-- MODIFIED: if the load lands on an already-active player with no password,
-- skip the splash and resume play directly.
-- ===========================================================================
function OnLoadScreenClose()
	local pPlayer = Players[ Game.GetLocalPlayer() ];
	if (pPlayer ~= nil) then
		if (pPlayer:IsTurnActive()) then
			OnLocalPlayerTurnBegin();
			return;
		end
	end
	bPlayerChanging = true;
	UIManager:QueuePopup( ContextPtr, PopupPriority.PlayerChange);
end

function OnShow()
	ShowTurnControls();
end

function ShowTurnControls()
	EffectsManager:PauseAllEffects();
	LuaEvents.PlayerChange_Show();
	if(not bPlayerChanging) then
		local localPlayerID = Game.GetLocalPlayer();
		local localPlayer = PlayerConfigurations[localPlayerID];
		Controls.PlayerChangingText:SetHide(true);
		Controls.PopupAlphaIn:SetHide(false);
		Controls.PasswordEntry:SetText("");
		Controls.PopupAlphaIn:SetToBeginning();
		Controls.PopupAlphaIn:Play();
		Controls.PopupSlideIn:SetToBeginning();
		Controls.PopupSlideIn:Play();
		if(localPlayer:GetHotseatPassword() == "") then
			Controls.PasswordStack:SetHide(true);
			Controls.OkButton:SetDisabled(false);
		else
			Controls.PasswordStack:SetHide(false);
			Controls.OkButton:SetDisabled(true);
			Controls.PasswordEntry:TakeFocus();
		end
		UpdateBottomButtons();
	else
		Controls.PlayerChangingText:SetHide(false);
		Controls.PopupAlphaIn:SetToBeginning();
		Controls.PopupSlideIn:SetToBeginning();
		Controls.PopupAlphaIn:SetHide(true);
	end
end

function UpdateBottomButtons()
	ShowOkButton();
	ShowSaveButton();
	Controls.BottomButtonsStack:CalculateSize();
	Controls.BottomButtonsStack:ReprocessAnchoring();
end

function ShowOkButton()
	local showOk = GameConfiguration.IsHotseat();
	Controls.OkButton:SetHide(not showOk);
end

function ShowSaveButton()
	local showSave = GameConfiguration.IsHotseat();
	Controls.SaveButton:SetHide(not showSave);
end

function SetPause(bNewPause)
	local localPlayerID = Game.GetLocalPlayer();
	local localPlayerConfig = PlayerConfigurations[localPlayerID];
	if (localPlayerConfig ~= nil) then
		local oldPause = localPlayerConfig:GetWantsPause();
		if(bNewPause ~= oldPause) then
			local bIsTurnActive = Players[localPlayerID]:IsTurnActive();
			if (bIsTurnActive or bNewPause == false) then
				localPlayerConfig:SetWantsPause(bNewPause);
				Network.BroadcastPlayerInfo();
			end
		end
	end
end

function OnPasswordEntryStringChanged(passwordEditBox)
	local localPlayerID = Game.GetLocalPlayer();
	local localPlayer = PlayerConfigurations[localPlayerID];
	local password = "";
	if(passwordEditBox:GetText() ~= nil) then
		password = passwordEditBox:GetText();
		if(password == localPlayer:GetHotseatPassword()) then
			Controls.OkButton:SetDisabled(false);
		else
			Controls.OkButton:SetDisabled(true);
		end
	end
end

function OnPasswordEntryCommit()
	local localPlayerID = Game.GetLocalPlayer();
	local localPlayer = PlayerConfigurations[localPlayerID];
	local password = "";
	if(Controls.PasswordEntry:GetText() ~= nil) then
		password = Controls.PasswordEntry:GetText();
		if(password == localPlayer:GetHotseatPassword()) then
			OnOk();
		end
	end
end

function OnTeamVictory(team, victory, eventID)
	if(not ContextPtr:IsHidden()) then
		UIManager:DequeuePopup(ContextPtr);
		Events.LocalPlayerTurnBegin.Remove(OnLocalPlayerTurnBegin);
		Events.LocalPlayerTurnEnd.Remove(OnLocalPlayerTurnEnd);
		Events.LoadScreenClose.Remove(OnLoadScreenClose);
	end
end

function OnPlayerDestroyed(playerID)
	local localPlayerID = Game.GetLocalPlayer();
	if(localPlayerID == playerID) then
		bLocalPlayerDestroyed = true;
	end
end

function OnRequestClose()
	LuaEvents.PlayerChange_OpenInGameOptionsMenu();
end

function OnEndGameMenu_OneMoreTurn()
	print("OnEndGameMenu_OneMoreTurn");
	Events.LocalPlayerTurnBegin.Add(OnLocalPlayerTurnBegin);
	Events.LocalPlayerTurnEnd.Add(OnLocalPlayerTurnEnd);
	Events.LoadScreenClose.Add(OnLoadScreenClose);
end

function OnEndGameMenu_ViewingPlayerDefeat()
	print("OnEndGameMenu_ViewingPlayerDefeat");
	if(not ContextPtr:IsHidden()) then
		UIManager:DequeuePopup(ContextPtr);
	end
end

function Initialize()
	Events.LocalPlayerTurnBegin.Add(OnLocalPlayerTurnBegin);
	Events.LocalPlayerTurnEnd.Add(OnLocalPlayerTurnEnd);
	Events.LoadScreenClose.Add(OnLoadScreenClose);
	Events.TeamVictory.Add(OnTeamVictory);
	Events.PlayerDestroyed.Add(OnPlayerDestroyed);
	Events.UserRequestClose.Add( OnRequestClose );

	LuaEvents.EndGameMenu_OneMoreTurn.Add(OnEndGameMenu_OneMoreTurn);
	LuaEvents.EndGameMenu_ViewingPlayerDefeat.Add(OnEndGameMenu_ViewingPlayerDefeat);

	ContextPtr:SetShowHandler( OnShow );
	ContextPtr:SetInputHandler( OnInputHandler );

	Controls.SaveButton:RegisterCallback(Mouse.eLClick, OnSave);
	Controls.OkButton:RegisterCallback(Mouse.eLClick, OnOk);
	Controls.MenuButton:RegisterCallback( Mouse.eLClick, OnMenu );
	Controls.PasswordEntry:RegisterStringChangedCallback(OnPasswordEntryStringChanged);
	Controls.PasswordEntry:RegisterCommitCallback(OnPasswordEntryCommit);

	print("civ6-async: PlayerChange.lua replacement loaded");
end

if GameConfiguration.IsHotseat() then
	Initialize();
end
