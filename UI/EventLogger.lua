-- civ6-async EventLogger
--
-- Emits structured events to Lua.log so the desktop helper can read
-- authoritative game state (turn, active player, save metadata) without
-- having to parse the binary .Civ6Save format.
--
-- Format: every line starts with the literal prefix "civ6-async|" so the
-- helper can grep for them regardless of which Civ context wrote them.
-- Subsequent fields are pipe-delimited key=value pairs.
--
-- Example:
--   ActionPanel: civ6-async|save_complete|turn=42|player=arin|at=2026-05-03T18:00:00Z

local PREFIX = "civ6-async|"

local function nowIso()
    -- os.date("!%Y-%m-%dT%H:%M:%SZ") would be ideal but `os` is sandboxed
    -- in Civ Lua. Best we can do is a relative timestamp from the game's
    -- own clock.
    return tostring(Game.GetCurrentGameTurn())
end

local function safe(s)
    if s == nil then return "" end
    return tostring(s):gsub("|", "_"):gsub("[\r\n]", " ")
end

local function emit(eventName, fields)
    local parts = { PREFIX .. eventName }
    for k, v in pairs(fields) do
        parts[#parts + 1] = k .. "=" .. safe(v)
    end
    print(table.concat(parts, "|"))
end

local function localPlayerName()
    local id = Game.GetLocalPlayer()
    if id == nil or id < 0 then return "?" end
    local cfg = PlayerConfigurations[id]
    if cfg == nil then return tostring(id) end
    -- Prefer the explicit display name; fall back to player id.
    local name = cfg:GetPlayerName()
    if name == nil or name == "" then return tostring(id) end
    return name
end

local function emitTurnState(eventName)
    emit(eventName, {
        turn       = Game.GetCurrentGameTurn(),
        player     = localPlayerName(),
        playerId   = Game.GetLocalPlayer(),
        isHotseat  = tostring(GameConfiguration.IsHotseat()),
    })
end

local function OnSaveComplete()
    emitTurnState("save_complete")
end

local function OnLocalPlayerTurnBegin()
    emitTurnState("local_turn_begin")
end

local function OnLocalPlayerTurnEnd()
    emitTurnState("local_turn_end")
end

local function OnLoadScreenClose()
    emitTurnState("game_loaded")
end

emit("logger_loaded", { version = "1" })

if Events.SaveComplete           then Events.SaveComplete.Add(OnSaveComplete) end
if Events.LocalPlayerTurnBegin   then Events.LocalPlayerTurnBegin.Add(OnLocalPlayerTurnBegin) end
if Events.LocalPlayerTurnEnd     then Events.LocalPlayerTurnEnd.Add(OnLocalPlayerTurnEnd) end
if Events.LoadScreenClose        then Events.LoadScreenClose.Add(OnLoadScreenClose) end
