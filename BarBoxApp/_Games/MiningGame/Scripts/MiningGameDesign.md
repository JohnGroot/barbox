- An Idle Game called MiningGame where the Player accumulates Gems over realtime and can either trade those Gems for Upgrades or      
  Credits (to be used in other barbox games)
- Gems are earned at a pace of 1 tick every 2 hours. The amount of time for a tick should be customizable
- Every tick a (customizable) number of Gems is added to a count that can be Extracted to the Player's account data. 
  Those # of gems ready for Extraction are LOCAL to the machine being played on
- The Player must Log In in-order to access their account's gems or extract anything             
- Each BarBox Location has one of several Gem Types assigned to them representing what gem type is being mined AND the 
  resource required for purchasing Credits and Upgrades
- There should be a Max Capacity of Gems ready to be extracted, but not of how many gems can be held in the global inventory
- It should cost 150 gems to purchase a Credit with unlimited purchases (no recharge timers)
- Upgrades should allow for the incremental improvement of Gem Capacity (Increases your max gem capacity), Gem Mining
Amount (Increases amount mined every Tick), Gem Mining Speed (Speeds up your mining Tick)
- Upgrades should "level up" every time they're purchased, with a limit of 15 levels (so once level 15 is met it can't be upgraded anymore)
- These upgrades are Local to the Location that they're purchased on, as are the levels. So Each Machine you upgrade disparately
- Every 5 upgrade "levels" they entire a new tier with a more difficult price: 
  - Tier 1 is JUST an amount of the gem of the Location
  - Tier 2 is amounts of the location's gem and a randomized other gem type that is different from the current location's
  - Tier 2 is amounts of the location's gem and two randomized other gem types that are different from the current location's
- When the Player logs into a location for the first time they should have their gem capacity maxed out, ready to be extracted.
- The starting gem capacity should be enough to purchase a credit or 1 upgrade (so 150 gems)
- For Progression, it should start out Time Consuming to purchase credits and Frequent to purchase Upgrades
    - With Each Upgrade Purchase it should take slightly less time to purchase credits and Slightly more time to purchase an Upgrade (which are individually scaled)
    - The timespans I'm thinking of are like: 1.5 weeks to get enough gems to purchase a Credit at the start, 0.25 weeks to purchase a Credit when maxed out
    - For Upgrades I'm thinking it should take 0.5 weeks to get enough for a level 1 upgrade, and a 1.5-2 months to get a level 15 upgrade
- For UI there should be these elements in these groupings: 
  - a Progress Bar and Time Counter that ticks every frame displaying how long until the next Gem Tick
  - Buttons to Extract Gems and Purchase Credits, disabled when neither is possible
  - Buttons to Purchase Upgrades, showing the upgrade type, cost and current level. Disabled when not affordable
- All UI should be disabled and not updating when the Player is not logged in
