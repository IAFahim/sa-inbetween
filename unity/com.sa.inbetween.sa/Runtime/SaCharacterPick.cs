using Unity.Collections;
using Unity.Entities;

namespace SA.Inbetween.Clips
{
    public static class SaCharacterPick
    {
        public static bool TryFind(EntityManager manager, string character, out Entity entity, out string status)
        {
            entity = Entity.Null;
            using var query = manager.CreateEntityQuery(ComponentType.ReadOnly<SharedClipAsset>(), ComponentType.ReadOnly<SharedContribution>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            if (entities.Length == 0)
            {
                status = "Waiting for the character...";
                return false;
            }

            if (string.IsNullOrEmpty(character))
            {
                if (entities.Length == 1)
                {
                    entity = entities[0];
                    status = string.Empty;
                    return true;
                }

                status = "Found " + entities.Length + " SA characters. Type the name of the one this player drives in Character (the object that has Shared Family Authoring).";
                return false;
            }

            var name = new FixedString64Bytes();
            name.CopyFromTruncated(character);
            var hash = Names.Hash(name);
            var matches = 0;
            var match = Entity.Null;
            for (var i = 0; i < entities.Length; i++)
            {
                if (!manager.HasComponent<SharedRig>(entities[i]) || manager.GetComponentData<SharedRig>(entities[i]).NameHash != hash) continue;
                matches++;
                match = entities[i];
            }

            if (matches == 1)
            {
                entity = match;
                status = string.Empty;
                return true;
            }

            status = matches == 0
                ? "No SA character named '" + character + "' among " + entities.Length + ". Character must be the name of the object that has Shared Family Authoring."
                : "Found " + matches + " SA characters named '" + character + "'. Give each a different name.";
            return false;
        }
    }
}
