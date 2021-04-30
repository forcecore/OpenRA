MissionAccomplished = function()
	Media.PlaySpeechNotification(player, "MissionAccomplished")
end

MissionFailed = function()
	Media.PlaySpeechNotification(player, "MissionFailed")
end

AllToHunt = function()
	local sovietArmy = ussr.GetGroundAttackers()

	Utils.Do(sovietArmy, function(unit)
		unit.Hunt()
	end)
end

Tick = function()
	-- ussr.Resources = ussr.Resources - (0.01 * ussr.ResourceCapacity / 25)
end

WorldLoaded = function()
	player = Player.GetPlayer("Greece")
	ussr = Player.GetPlayer("USSR")

	Trigger.OnObjectiveAdded(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "New " .. string.lower(p.GetObjectiveType(id)) .. " objective")
	end)
	Trigger.OnObjectiveCompleted(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "Objective completed")
	end)
	Trigger.OnObjectiveFailed(player, function(p, id)
		Media.DisplayMessage(p.GetObjectiveDescription(id), "Objective failed")
	end)

	Trigger.OnPlayerLost(player, MissionFailed)
	Trigger.OnPlayerWon(player, MissionAccomplished)

	KillAllObjective = player.AddPrimaryObjective("Kill Everything.")

	Trigger.AfterDelay(DateTime.Seconds(1), AllToHunt)

	Camera.Position = InsertionLZ.CenterPosition
end
