namespace Server.SoundPlayer;

public interface IDirectionalSoundSource
{
    /// <summary>
    /// Set the channel mapping for the sound source.
    /// The mapping array should have a length equal to the number of channels in the output format. 
    /// Each element in the array represents the gain for the corresponding channel, 
    /// where 1.0 means full volume and 0.0 means silence.
    /// </summary>
    /// <param name="mapping">array of multipliers (gains)</param>
    void SetChannelMap(float[] mapping);  
}
