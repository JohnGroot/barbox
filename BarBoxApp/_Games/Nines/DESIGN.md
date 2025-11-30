*Design Details*
- This is a card game using a standard 52-card deck, Ace is High
- To Set Up the game the cards are shuffled and 9 cards are drawn and laid down face up in a 3x3 grid
- The game is played in turns, with a maximum of 8 players taking turns
- Every turn a different player points to one of the 9 FACE UP stacks of cards on the table and declares whether the next drawn card will be greater than, less than, or equal to the pointed target card.
- If the player is WRONG the target stack of cards is flipped over & is taken out of play
- If the player calls "Same" and is correct they select a facedown stack of cards to flip over, otherwise the target gets flipped over
- In any case if the player guesses CORRECTLY the card gets placed on the top of the target stack
- If the player gets a correct Same call they get to draw again OTHERWISE the turn to draw gets passed to the next player
- If all stacks get flipped over the players LOSES
- The players WIN if they run out of cards to draw
- The game can be played for free, but if 2 MACHINE CREDITS used then you can play for the venue's pot of credits
  - Half of the credits paid go to a pot on the machine for the game
  - Percentage for the pot and the cost should be customizable values for us as developers
  - Upon winning, the jackpot of credits is split between all players evenly, rounding up

*UX Details*
- Main screen is just an empty "playing field" taking up most of the screen. this will frame the cards
- On this main screen there will be in this order vertically: 
  - the game name as a header,
  - two buttons, one to start a free game and one to Play for the Jackpot (this one should be disabled if there aren't enough credits or nobody is logged in) 
  - a big display of the JACKPOT machine pot of credits just below the center playing field frame
- On the Side of the screen there will be a list of names of players logged in
  - Names should be able to be Pressed to bring up a modal of options to Buy Credits, Deposit Credits & Log Out  
  - Underneath the last login username there will be a "login" button that players can use to log one more player in, unless 8 players are logged in
- Once a game is started, that main menu UI should be hidden/disabled EXCEPT the Jackpot value, a Deck visual should appear and then a dealing sequence should execute to deal out the first 9 cards
- After the first 9 cards are dealt the game waits until the user taps on a stack on the playing field.
- Once the player selects a stack they should see an exciting popup menu where they select Higher, Lower, or Same (plus an X close menu button)
- When they make a choice, the menu dismisses and then a card dramatically should tween from the deck a the revealed card to that stack
- There should be a NICE or OOF success/failure message of some kind that pops up after the result is seen
- When a stack flips it should do so visually ideally as a tween
- The number of cards left should be visible next to the Deck
- Cards should be programmatically drawn (for now) using the 2dCanvas similar to the carromsgame board 
  - They should feature an outline, its value in the corners, and symbols anchored in the center
- Winning or Losing should bring up a Results Modal. If it was a Paid Jackpot Round then their account should be credited

*Technical Details*
- We should think about how we can abstract this card game paradigm to be reimplemented in different contexts.
- This UX sequencing around turns should be considered as part of this abstraction. 
  - I'm imagining a queue of Actions that gets executed with delays, that can be queued up as they're executing, maybe just a wrapper of the tween system to handle this.
- Part of what I want is a game implementation that is still sequential, testable and traceable in its state whiel still having async logic and delays. 
  - Deferred calls are not the ideal in this case per usual
- Like Carroms, when the game is exitted all players are logged out except the earliest person who logged in
- We should track the succes / failures, wins/losses of each account as analytics events
