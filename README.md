This project demonstrates a chracter in a 2D platformer that can move around and jump in the world. It demonstrates a few different features of this character, including the ability to move up and down slopes up to a certain degree.

Controls:

Left/Right Arrows: move the character horizontally both on the ground and in the air

Space: jump when the character is on the ground (or was on the ground within the Coyote Time constant). Hold the key longer in order to jump higher.

Down Arrow: drop through a "one-way" platform when the chracter is on top of one

The character:

![Character](Assets/Textures/character.png)

One-Way platforms:

![One-Way Platform](Assets/Textures/platform.png)

Solid platforms:

![Solid Platform](Assets/Textures/solid2.png)

The physics of the character is handled without the use of Rigidbody, but uses a custom behavior called CharacterPhysicsBody instead. This has properties for controlling the speed, acceleration, jump height, etc. of the character.