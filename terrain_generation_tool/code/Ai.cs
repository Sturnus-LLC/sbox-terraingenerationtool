using Sandbox;

public sealed class Ai : Component
{
	protected override void OnUpdate()
	{
		NavMeshAgent agent = this.GetComponent<NavMeshAgent>();

		// Sets the target position for the agent. It will try to get there
		// until you tell it to stop.
		agent.MoveTo( Scene.GetAllComponents<PlayerController>().First().WorldPosition );


		// The agent's actual velocity
		var velocity = agent.Velocity;
	}
}
