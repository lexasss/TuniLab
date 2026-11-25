# TuniLab Server

The tool initiates the following [gRPC services](../Core/README.md):

- [Web-cameras](Camera/README.md)
- [CARLA simulator](Carla/README.md)
- [Simucube steering wheel and active pedals + MECA (passive) pedals](Driving/README.md)
- [Leap Motion hand tracker](LeapMotion/README.md)
- [MyGaze eye tracker](MyGaze/README.md)
- [Joysticks and gamepads](Pointing/README.md)
- [Screens](Screen/README.md)
- [SmartEye eye tracker](SmartEye/README.md)
- [Sound devices](SoundPlayer/README.md)
- [Stream Deck devices](StreamDeck/README.md)
- [TensionR belts](TensionR/README.md)
- [Tobii EyeX eye tracker](TobiiEyeX/README.md)

The server supposes to work with a single client app. It is advised to restart the server before running a client app, as the previous session may leave the server in an undesired state. (Future version must FIX this issue).