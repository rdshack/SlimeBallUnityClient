using System;
using System.Collections;
using System.Collections.Generic;
using ecs;
using UnityEngine;

namespace Indigo.EcsClientCore
{
    public class KeyboardReader
    {
        public enum MoveKeyStyle
        {
            WASD,
            ARROWS
        }
        
        private List<KeyCode> _lastKeyXAxis = new List<KeyCode>();
        private List<KeyCode> _lastKeyZAxis = new List<KeyCode>();
        private bool          _leftMousePressed;
        private bool          _spacePressed;

        private KeyCode _moveLeft;
        private KeyCode _moveRight;
        private KeyCode _moveUp;
        private KeyCode _moveDown;
        private KeyCode _jump;

        public KeyboardReader(MoveKeyStyle moveKeyStyle)
        {
            if (moveKeyStyle == MoveKeyStyle.WASD)
            {
                _moveLeft = KeyCode.A;
                _moveRight = KeyCode.D;
                _moveUp = KeyCode.W;
                _moveDown = KeyCode.S;
            }
            else
            {
                _moveLeft = KeyCode.LeftArrow;
                _moveRight = KeyCode.RightArrow;
                _moveUp = KeyCode.UpArrow;
                _moveDown = KeyCode.DownArrow;
            }

            _jump = KeyCode.Space;
        }

        private void OnAttackButtonClicked()
        {
            _leftMousePressed = true;
        }

        public void ReadLatest()
        {
            if (Input.GetKeyDown(_moveUp))
            {
                _lastKeyZAxis.Insert(0, _moveUp);
            }
            else if (Input.GetKeyUp(_moveUp))
            {
                _lastKeyZAxis.Remove(_moveUp);
            }
            
            if (Input.GetKeyDown(_moveDown))
            {
                _lastKeyZAxis.Insert(0, _moveDown);
            }
            else if (Input.GetKeyUp(_moveDown))
            {
                _lastKeyZAxis.Remove(_moveDown);
            }
            
            if (Input.GetKeyDown(_moveLeft))
            {
                _lastKeyXAxis.Insert(0, _moveLeft);
            }
            else if (Input.GetKeyUp(_moveLeft))
            {
                _lastKeyXAxis.Remove(_moveLeft);
            }
            
            if (Input.GetKeyDown(_moveRight))
            {
                _lastKeyXAxis.Insert(0, _moveRight);
            }
            else if (Input.GetKeyUp(_moveRight))
            {
                _lastKeyXAxis.Remove(_moveRight);
            }

            if (Input.GetKeyDown(_jump))
            {
                _spacePressed = true;
            }
        }

        public void Clear()
        {
            _leftMousePressed = false;
            _spacePressed = false;
        }

        public bool IsLeftMousePressed()
        {
            return _leftMousePressed;
        }

        public bool IsSpacePressed()
        {
            return _spacePressed;
        }

        public Vector2Int GetLastMoveInput()
        {
            //return new Vector2Int(1, 0);
            
            int xAxis = 0, zAxis = 0;
            if (_lastKeyXAxis.Count != 0)
            {
                xAxis = _lastKeyXAxis[0] == _moveLeft ? -1 : 1;
            }
            
            if (_lastKeyZAxis.Count != 0)
            {
                zAxis = _lastKeyZAxis[0] == _moveDown ? -1 : 1;
            }

            return new Vector2Int(xAxis, zAxis);
        }
    }   
}
