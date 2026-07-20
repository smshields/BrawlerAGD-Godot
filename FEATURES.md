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


# Fast Fall/Crouch/Directional Influence

The fast fall mechanic allows a player to increase the rate of their descent by holding down while in the air. The crouch mechanic allows a player to "squish" their characters downwards by some percentage. Every character has both a fast fall and a crouch, and these parameters will live alongside basic character constraints instead of as an assignable button. 



### Fast Fall
- Global 
	- You can only fast fall in the air.
	- Fast falling is activated by pressing/holding down.
	- Fast falling can be used even during all states in air aside from dash and attack execution and stun (jumps exhausted, warm-up, cool-down can all be fast-fallen)
- Parameters
	- Accelleration
		- Affects the rate at which the character falls while pressing down. Can never be lower than the default fall rate for a character.
- Agent
	- Fast fall is effectively another dodge action. As such, if a character is in the air and vulnerable to an attack, it should be one of potential options to dodge, and should be favored if warm-up/cool-down/jumps exhausted is active.
	- Fast fall can be used to close distance with an opponent below you for an attack, and should occasionally be selected to get in range of an opponent during

### Crouch
- Global
	- Crouching reduces character height only by a ratio.
	- Crouching changes friction on the floor - could be positive or negative; if you are running and crouch, you might slow down quickly or slide farther.
	- Crouching speed should animate, and generally be very fast (but not instantaneous).
	- Crouching can be used to avoid dying if on the floor to slow down momentum (if it does slow down) or to approach (if it speeds up)
	- Only allowed while touching the floor
	- Crouch can be used during idle only.
	- Hit-box should be changed alongside the player
	- if a character attacks while crouching, they first return to their normal size before executing the attack. The time to return to full size must be included before attacking, and it cannot be cancelled or accept any inputs while doing it.
	- No inputs are possible between full size and full crouch.
	- Crouching has its own state.
- Parameters
	- Accelleration Change
		- Scalar that decides how much a character positively/negatively accellerates if crouching while moving
	- Speed
		- Determines how quickly a character reaches final crouch and returns to full stand.
	- Height Ratio
		- Determines the ratio of height when crouching (must be less than the character size - e.g. always less than 1).
- Agent
	- The agent should include crouching as an option to dodge incoming attacks (alongside moving away, jumping, dashing)
	- The agent should use crouch to reduce knockback off a stage at high percentages when knocked along the ground if the accelleration change slows them down
	- The agent should use crouch to approach quickly for damage opportunities if the accelleration change speeds them up.

### Directional Influence
- Global
	- Directional influence impacts the direction a character flies when hit. 
	- Directional influence is only taken into account the moment that a character is hit and begins to receive knockback.
	- Directional influence happens regardless of character state.
	- Directional influence is minor (it can't override knockback magnitude or direction entirely), but gives a player a small chance of not flying entirely off screen.
	- Directional influence is difficult, and shouldn't be perfect-reaction for agents during evolution.
	- We should have a new UI elements that show the direction influence when watching so we can debug. Place it near existing UI elements.
	- Influence should be applied at a very small rate 
- Parameters
	- Directional influence
		- Determines how much a key press impacts the direction of knockback. Should not be able to substantially impact knockback. Should be a proportion based on 
	- Knockback Reduction
		- Determines how much a knockback is reduced if the player is holding a direction opposite of the hit itself.
- Agent
	- Agents should be imperfect - they might still be holding a direction they were holding before they were attempting to influence, but they may also directionally influence perfectly.
	- Agents should try and influence towards the farthest line to die to minimize risk of death.


# Projectiles
Projectiles are a new type of attack - they should have similar properties to melee attacks (warm-up, execute, cool-down) but instead of generating a sprite next to them, they should generate a moving object that will follow some trajectory. This is the most complicated feature yet, and will likely carry the most parameters along side it.

- Global
	- Projectiles start from a point on the character and travel in a direction.
	- They have many of the same properties as attacks - warm-up, execute, cool-down. 
	- This is a new state. 
	- We are not using sprites for the projectile for now - we will use generic shapes as placeholders.
	- We will have multiple types of path shapes.
	- Projectiles despawn after they go off-screen past the boundary of the level.
	- Transparency should be incorporated during damage decay only. If a projectile does not decay in damage, it should stay solid and visible.
	- Projectiles need to be differentiated from shields.
	- Execution state is independent from how long the projectile is on screen (e.g. multiple projectiles may be on screen at the same time)
	- Projectiles should never damage the user on fire, but they might.
	- Projectiles should be assigned as an attack type and be considered one.
- Parameters
	- Path Shape
		- Sinusoidal, Linear, Exponential/Quadratic
		- Determines the path of the projectile as it moves.
	- Path Shape Scalar
		- Determines characteristics of non-linear paths
		- E.G. frequency for waves, scalars for exponents/quadratics.
	- Time to Decay
		- Determines how long a projectile will stay on screen before it disappears.
	- Velocity
		- How quickly a projectile travels. The starting speed of the projectile.
	- Does accelerate
		- Determines if a projectile has acceleration (bool)
	- Acceleration
		- How much a projectile speeds up/slows down while it travels.
		- Only relevant if accelerate is true.
	- Affected by Gravity
		- Determines if the projectile is impacted by gravity or not.
	- Warm-Up
		- How much time it takes to start shooting a projectile
	- Execute
		- How long the execution step of the projectile takes
	- Cool-Down
		- How much time it takes to recover before other actions are possible.
	- Hitbox Size
		- How big the hitbox of the projectile is
		- Never should be larger than the shooting character.
	- Hitbox Shape
		- Basic shape of the projectile
		- Square, Circle, Triangle
	- Hitbox Rotation
		- Determines if the projectile is rotating or not as it travels (bool)
	- Hitbox Rotation rate
		- Determines how quickly the rotation is for the projectile.
	- Knockback Magnitude
		- How much knockback a player will take when being hit by the projectile.
	- Knockback Direction
		- The direction of knockback on collision with a projectile
		- Knockback calculation should match the same behaviors as a melee attack, except it's applied to the disjointed hitbox of the projectile.
	- Damage Decay
		- Determines if the projectile damage/knockback is decreased as the projectile travels (bool)
	- Rate of Damage Decay
		- The speed at which damage/knockback is decreased (if damage decay is on)
	- Damage
		- The damage the projectile deals.
	- Hits Self
		- Determines if the projectile hits the originating player if they run into it after using it
	- Launch location
		- The point around the player that the projectile originates from. It should be overlapping with some element of the player (such that you are not starting a projectile from a disjoint position)
- Agent
	- Agents should attempt to shoot projectiles when at a distance, predicting a loose range of hits based on projectile shape
	- Agents should avoid using projectiles at close range
	- Agents should attempt to dodge incoming projectiles
	- Zoning should be possible but not overly selected for