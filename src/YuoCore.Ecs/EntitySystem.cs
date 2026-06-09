namespace YuoCore.Ecs
{
    public class EntitySystem : YuoSystem<EntityComponent>, IAwake
    {
        public override string Group => SystemGroupConst.Main;

        public override void Run(EntityComponent component)
        {
        }
    }
}