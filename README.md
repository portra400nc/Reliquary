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
| Create new template | Y | N |
| Save worn gear as template | View | G |
| Change an item | A on a slot | Enter on a slot |
| Apply template | X | F |
| Rename or delete | Menu | R |

## Teams tab

Press LT or RT to switch tabs (Q or E on a keyboard). Save the party's worn gear as a team with Y. A team template names one character template per member, so a change to that template reaches every team that uses it. Choose a member with left and right, and their template with up and down.

Reliquary will not apply a team when two members need the same item.

## Applying

Reliquary lists every change first. Wearing an item another character holds gives that character your old item, as in the game. Press Y right after applying to undo it. Items you no longer own are skipped.

## Limits

- The comparison adds up the items' own stats. It leaves out set bonuses and the character's base stats.
- Set names are not shown yet, only the piece count.
- The game's controller navigation does not reach the tile, so controllers use Y.
- Artifacts cannot be taken off, because the mod has no request for that.

Templates are saved per account in `UserData/Reliquary/`.
