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


# Dash Mechanic

The dash mechanic allows a player to quickly move from one spot to another with a varying amount of speed and invulnerability. The dash has many use cases - it can help get back to a platform, it can move away from an opponent, or it can close distance to attack. For now, like the shield, we will assign it to a single button and clamp it to it (right shoulder button) while also developing this so that we can make it assignable in the future during evolutions.

- We will make some small changes to controls and state management to make dashes work as intended. Namely:
	- The dash must be a new state. Players cannot attack, shield, jump during a dash.
	- Dashes have a warm up
	- The dash introduces invincibility frames - we will play with the ability to turn them on/off depending on the stage of the dash. This allows the dash to be more useful for recovery/evasion.
	- Jumping mechanics change - The following are allowable states. Players are not completely exhausted until all three movements are made. 
		- dash - jump - jump (players dash into the air by holding up or up/left or up/right)
		- jump - dash - jump
		- jump - jump - dash
		- Doing any of the above puts you back into the jumps exhausted state, where further actions are not possible.
	- Dashes are directional - they will move the character in the direction that they are holding. If they are not holding any direction, they move horizontally in the direction the player is holding.
	- Dashes have a duration - during this, players cannot change their direction until the dash is over.
	- If a player dashes collides with an opponent
- Like attacks and shields, this mechanic will be parameterized with the following behaviors.
	- Warm-up time - same behavior as shield/attack
		- Decent range, but should be clamped. We're looking for something that enables fast, short dashes, or slow, long dashes, or any permutation there of. Note: there is no cool-down time for dashes.
	- Acceleration 
		- how much acceleration performing a dash causes in the direction the player is holding
	- Duration 
		- how long the dash lasts
		- During this time, the player has no ability to input controls (e.g. change direction, execute an attack, etc.)
		- If the dash is causing the jump-exhausted state, the character should enter the state after the duration is completed - no actions should be possible between direction and dash.
	- Warm-up invulnerable
		- T/F - if active, during the warm-up of the dash, the player becomes invulnerable to attacks.
	- Duration invulnerable
		- T/F - if active, during the dash, the player becomes invulnerable to attacks.
- Agent Behaviors
    - Agents should use dash to recover and return to platforms.
    - Agents should use dash to avoid high-risk incoming attacks.
    - Agents should use dash to close distance when a kill is likely.
    - Dashes should be avoided in the air (to avoid entering jump exhausted state) unless necessary to recover or a promising kill is possible.