/// <summary>
/// Controls cargo elevator
/// </summary>
public enum ElevatorStatus
{
	TravellingUp = 0,
	IsUp = 1,
	TravellingDown = 2,
	IsDown = 3
}

/// <summary>
/// For shuttles that move between two destinations. Will be used by landing shuttles, and temporarily used by evac shuttle
/// </summary>
public enum ShuttleStatus
{
	TravellingToA = 0,
	AtA = 1,
	TravellingToB = 1,
	AtB = 2,
}
