using System;
using UnityEngine;

namespace S014.MobileAR
{
    [Serializable]
    public sealed class S014Waypoint
    {
        public int index;
        public Vector3 worldPosition;
        public string instruction;
        public string floor = "Ground";
        public bool isFloorTransition;
        public string landmark;
        public string destinationFloor;

        public S014Waypoint(int index, Vector3 worldPosition, string instruction, string floor, bool isFloorTransition = false, string landmark = "", string destinationFloor = "")
        {
            this.index = index;
            this.worldPosition = worldPosition;
            this.instruction = instruction;
            this.floor = floor;
            this.isFloorTransition = isFloorTransition;
            this.landmark = landmark;
            this.destinationFloor = destinationFloor;
        }

        public S014Waypoint Copy(int newIndex)
        {
            return new S014Waypoint(newIndex, worldPosition, instruction, floor, isFloorTransition, landmark, destinationFloor);
        }
    }
}
