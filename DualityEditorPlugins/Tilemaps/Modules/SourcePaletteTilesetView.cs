﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

using Duality.Resources;
using Duality.Plugins.Tilemaps;


namespace Duality.Editor.Plugins.Tilemaps
{
	public class SourcePaletteTilesetView : TilesetView
	{
		private Rectangle selectedArea           = Rectangle.Empty;
		private Point     actionBeginTilePos     = Point.Empty;
		private bool      isUserSelecting        = false;

		public event EventHandler SelectedAreaChanged = null;
		public event EventHandler SelectedAreaEditingFinished = null;


		public Rectangle SelectedArea
		{
			get { return this.selectedArea; }
			set
			{
				if (this.selectedArea != value)
				{
					this.selectedArea = value;
					this.Invalidate();
					this.RaiseSelectedAreaChanged();
				}
			}
		}


		protected override void OnTilesetChanged()
		{
			base.OnTilesetChanged();
			this.SelectedArea = Rectangle.Empty;
			this.RaiseSelectedAreaEditingFinished();
		}
		protected override void OnPaintTiles(PaintEventArgs e)
		{
			Tileset tileset = this.TargetTileset.Res;

			// Draw hovered tile background
			if (this.Enabled && this.HoveredTileIndex != -1)
			{
				Point hoverPos = this.GetTileIndexLocation(this.HoveredTileIndex);
				e.Graphics.FillRectangle(
					new SolidBrush(Color.FromArgb(32, this.ForeColor)), 
					hoverPos.X - 1, 
					hoverPos.Y - 1, 
					this.TileSize.Width + 1, 
					this.TileSize.Height + 1);
			}

			// Paint the tile layer itself
			base.OnPaintTiles(e);

			// Draw hovered tile foreground
			if (this.Enabled && this.HoveredTileIndex != -1)
			{
				Point hoverPos = this.GetTileIndexLocation(this.HoveredTileIndex);
				e.Graphics.FillRectangle(
					new SolidBrush(Color.FromArgb(32, this.ForeColor)), 
					hoverPos.X - 1, 
					hoverPos.Y - 1, 
					this.TileSize.Width + 1, 
					this.TileSize.Height + 1);
				e.Graphics.DrawRectangle(
					new Pen(this.ForeColor), 
					hoverPos.X - 1, 
					hoverPos.Y - 1, 
					this.TileSize.Width + 1, 
					this.TileSize.Height + 1);
			}

			// Draw selection indicators
			if (this.Enabled && !this.selectedArea.IsEmpty)
			{
				int startIndex = this.GetTileIndex(this.selectedArea.X, this.selectedArea.Y);
				Point startPos = this.GetTileIndexLocation(startIndex);

				// "Grey out" everything except the current selection a little bit
				Region oldClip = e.Graphics.Clip;
				e.Graphics.SetClip(new Rectangle(
					startPos.X - 1, 
					startPos.Y - 1, 
					this.selectedArea.Width * (this.TileSize.Width + 1), 
					this.selectedArea.Height * (this.TileSize.Height + 1)), 
					System.Drawing.Drawing2D.CombineMode.Exclude);
				e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(64, this.BackColor)), this.ClientRectangle);
				e.Graphics.Clip = oldClip;

				// Draw the selected tile area
				e.Graphics.DrawRectangle(
					new Pen(this.BackColor), 
					startPos.X - 1, 
					startPos.Y - 1, 
					this.selectedArea.Width * (this.TileSize.Width + 1), 
					this.selectedArea.Height * (this.TileSize.Height + 1));
				e.Graphics.DrawRectangle(
					new Pen(this.ForeColor), 
					startPos.X - 2, 
					startPos.Y - 2, 
					this.selectedArea.Width * (this.TileSize.Width + 1) + 2, 
					this.selectedArea.Height * (this.TileSize.Height + 1) + 2);
				e.Graphics.DrawRectangle(
					new Pen(this.BackColor), 
					startPos.X - 3, 
					startPos.Y - 3, 
					this.selectedArea.Width * (this.TileSize.Width + 1) + 4, 
					this.selectedArea.Height * (this.TileSize.Height + 1) + 4);
				e.Graphics.DrawRectangle(
					new Pen(Color.FromArgb(128, this.BackColor)), 
					startPos.X - 4, 
					startPos.Y - 4, 
					this.selectedArea.Width * (this.TileSize.Width + 1) + 6, 
					this.selectedArea.Height * (this.TileSize.Height + 1) + 6);
			}
		}
		protected override void OnMouseDown(MouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Button == MouseButtons.Left)
			{
				int tileIndex = this.PickTileIndexAt(e.X, e.Y);
				if (tileIndex != -1)
				{
					this.actionBeginTilePos = this.GetTilePos(tileIndex);
					this.isUserSelecting = true;
					this.SelectedArea = new Rectangle(this.actionBeginTilePos.X, this.actionBeginTilePos.Y, 1, 1);
					this.HoveredTileIndex = -1;
				}
			}
		}
		protected override void OnMouseUp(MouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (!this.isUserSelecting)
			{
				this.SelectedArea = Rectangle.Empty;
			}
			this.actionBeginTilePos = Point.Empty;
			this.isUserSelecting = false;
			this.RaiseSelectedAreaEditingFinished();
		}
		protected override void OnMouseMove(MouseEventArgs e)
		{
			int tileIndex = this.PickTileIndexAt(e.X, e.Y);
			if (this.isUserSelecting)
			{
				if (tileIndex != -1)
				{
					Point tilePos = this.GetTilePos(tileIndex);
					Point selectionTopLeft = new Point(
						Math.Min(this.actionBeginTilePos.X, tilePos.X), 
						Math.Min(this.actionBeginTilePos.Y, tilePos.Y));
					Point selectionBottomRight = new Point(
						Math.Max(this.actionBeginTilePos.X, tilePos.X), 
						Math.Max(this.actionBeginTilePos.Y, tilePos.Y));
					this.SelectedArea = new Rectangle(
						selectionTopLeft.X,
						selectionTopLeft.Y,
						selectionBottomRight.X - selectionTopLeft.X + 1,
						selectionBottomRight.Y - selectionTopLeft.Y + 1);
				}
			}
			else
			{
				base.OnMouseMove(e);
			}
		}

		private void RaiseSelectedAreaEditingFinished()
		{
			if (this.SelectedAreaEditingFinished != null)
				this.SelectedAreaEditingFinished(this, EventArgs.Empty);
		}
		private void RaiseSelectedAreaChanged()
		{
			if (this.SelectedAreaChanged != null)
				this.SelectedAreaChanged(this, EventArgs.Empty);
		}
	}
}
