# BrawlerAGD Godot V0.2 Features
## Shield Mechanic

The Shield mechanic is a move that characters can use that is mapped to a button. It prevents damage, reduces knockback, and pushes back opponents who are immediately next to them. 

Shields degrade over time, and have relatively short wind-up/cool-down moments. Shields accelerate in degradation if they are hit by their opponent, and this degradation increases more based on the damage. If a shield is broken, the character who was shielding suffers a significant amount of stun.

Several pieces go into making the shield. To implement it, we need to do the following:

- Shielding needs to be a new state. 
	- A character cannot attack, move, or jump while shielding.
	- A character cannot shield while stunned, warming up, cooling down.
	- A character cannot shield in the air.
	- If a shield breaks, the character should be stunned for some time.
	- A shield should rapidly grow from the character to full size when activated.
	- A shield should rapidly shrink to the character when deactivated.
	- A shield only can protect where it covers. If an element of the character sprite is exposed, then the character will still be hit.
	- A shield can be directionally moved from the center of the character using the directional controls.
    - A shield must be parameterized so that it can be used for generation and assignable to any button.
- Shields should be defined by the following parameters and function alongside their descriptions:
	- Wind-up
		- The time it takes for the shield to become active. 
		- It should be shorter than attacks, but still have a variety of timing possibilities.
	- Cool-Down
		- The time it takes for the shield to deactivate and for the character to return to an idle state.
		- It should be shorter than attacks, but still have a variety of timing possibilities.
	- Initial Size
		- The diameter of the shield at it's largest size.
		- This is the shield without any degradation.
		- This should be no larger than twice the character size.
	- Hold Degradation Rate
		- The speed at which a shield reduces when held.
		- When the shield reaches 1/5th of the character size in radius, it should break.
	- Hit Degradation Rate
		- the speed at which a shield reduces when hit based on the damage dealt by an attack.
		- This reduces at different rates, but is always positively correlated with the damage dealt by the attack (more damage, more shield reduction).
		- If it reduces shield size below the threshold, it breaks similar to the hold degradation.
	- Knockback Reduction
		- The amount the shield reduces the knockback of a move. This should always reduce by a significant amount.
	- Shield Spacing
		- The ability for a shield to push an opponent back when activated.
		- At minimum, this always prevents an opponent from being within the shield.
		- At maximum, it should push the opponent back at a consistent distance. This knockback should never be enough to kill another character, it should be relatively low.
    - Shield Regeneration
        - The rate at which the shield returns to full size when it is not in use.
        - This means that if a shield is deactivated, then reactivated, it should start at it's current health level, not fresh.
	- Shield Control
		- The shield can be moved to cover different parts of the player. This uses the directional controls while the shield is active. Pointing in a direction moves the shield in that direction.
		- The edge of the shield can never move outside of the character's center.
		- Pushing a shield so that it overlaps the opponent should move the opponent so that it doesn't overlap with the opponent.
    - Agent Behavior
        - The agent should use the shield when it sees another character winding up for an attack in range of them.
        - It should have some priority trade off with moving away to dodge an attack. This choice should be driven by the weighted random nature
        - The agent should be less prone to shielding if the shield is close to breaking. 
        - The agent should try to move the shield towards the enemy if the shield is smaller than the character's hitbox.
        - If a player sees that the opponent is stunned from a shield break, they should attempt to use a powerful move.