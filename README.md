# Reliquary

Save weapon and artifact templates for each character and team, and apply them from a full-screen menu.

## Open Reliquary

- **Controller:** open the Paimon menu and press Y.
- **Mouse:** open the Paimon menu and click the Reliquary tile.

## Characters tab

Each character has their own templates. A slot left empty stays as it is when you apply the template.

| Action | Controller | Keyboard |
|---|---|---|
| Change character | LB, RB | Page Up, Page Down |
| List all characters | L3 | C, or the All characters button |
| Create new template | Y | N |
| Save equipped gear as template | View | G |
| Change an item | A on a slot | Enter on a slot |
| Apply template | X | F |
| Rename or delete | Menu | R |

## Character list

The list shows your party first, then the rest by level. The screen behind it follows the focused character, so you can see their templates before you choose.

| Action | Controller | Keyboard or mouse |
|---|---|---|
| Move | Left stick, D-pad | Arrow keys, or the mouse wheel |
| Open the character | A | Enter, or click the card |
| Go back to the character you started on | B | Esc |
| Filter by element and weapon | Y | N, or the funnel |
| Change the sort (Level, Rarity, Element) | X | F, or the sort box |
| Flip the order | Menu | R, or the arrows |

## Item picker

| Action | Controller | Keyboard or mouse |
|---|---|---|
| Filter | Y | N, or the Filter button |
| Sort artifacts | View | G, or the Sort button |
| Show only unequipped items | X | F, or the button |
| Use the item | A | Enter, or click the item |

Weapons filter by secondary attribute and always sort by level. Artifacts filter by set, and sort by up to three affixes: artifacts with the most chosen affixes come first, then the highest value of each in the order you chose them. Main stats count. In a filter or sort panel, X keeps your choice, B or Esc puts it back, and L3 or Backspace clears it.

## Teams tab

Press LT or RT to switch tabs (Q or E on a keyboard). Save the party's equipped gear as a team with Y. A team template names one character template per member, so a change to that template reaches every team that uses it. Choose a member with left and right, and their template with up and down.

Reliquary will not apply a team when two members need the same item.

## Applying

Reliquary lists every change first. Equipping an item another character holds gives that character your old item, as in the game. Right after applying, press Y or click Undo to put the old gear back. Deleting a template asks first. Items you no longer own are skipped.

## Limits

- The comparison adds up the items' own stats. It leaves out set bonuses and the character's base stats.
- The game's controller navigation does not reach the tile, so controllers use Y.
- Artifacts cannot be taken off, because the mod has no request for that.

Templates are saved per account in `UserData/Reliquary/`.
